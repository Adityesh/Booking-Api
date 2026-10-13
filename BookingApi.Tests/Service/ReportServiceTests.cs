using BookingApi.Data;
using BookingApi.Dto.Reporting;
using BookingApi.Service;
using BookingApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Tests.Service;

// Real Postgres (shared container). Reports aggregate over the WHOLE bookings table, so every test gets a calendar
// year of its own (1001, 1002, ...) and puts all of its bookings inside it. Nothing else in the suite writes
// bookings in those years, so counts and rankings here are exact. ReportsEndpointTests uses 1201+; keep the two
// ranges apart (this class must stay below ~190 tests).
[Collection("Postgres")]
public class ReportServiceTests(PostgresFixture fixture)
{
    private static int _yearCounter;
    private static int NextYear() => 1000 + Interlocked.Increment(ref _yearCounter);

    // ---------- helpers ----------

    private AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(fixture.ConnectionString)
        .UseSnakeCaseNamingConvention()
        .Options);

    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static DateOnly Day(int year, int month, int day) => new(year, month, day);

    private static ReportGetDto Window(DateTime from, DateTime to, int? top = null) =>
        new(From: new DateTimeOffset(from), To: new DateTimeOffset(to), Top: top);

    private async Task<int> SeedUserAsync()
    {
        await using var context = CreateContext();
        var user = new UserEntity
        {
            Username = $"u_{Guid.NewGuid().ToString("N")[..20]}",
            PasswordHash = "hash",
            Role = UserRole.User,
            IsActive = true
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task<int> SeedResourceAsync(string? name = null, bool isActive = true)
    {
        await using var context = CreateContext();
        var resource = new ResourceEntity
        {
            Name = name ?? $"resource_{Guid.NewGuid():N}",
            Type = ResourceType.Room,
            Capacity = 1,
            IsActive = isActive
        };
        context.Resources.Add(resource);
        await context.SaveChangesAsync();
        return resource.Id;
    }

    private async Task SeedBookingAsync(int resourceId, int userId, DateTime start, int minutes,
        BookingStatus status = BookingStatus.Confirmed)
    {
        await using var context = CreateContext();
        context.Bookings.Add(new BookingEntity
        {
            ResourceId = resourceId,
            UserId = userId,
            StartTime = start,
            EndTime = start.AddMinutes(minutes),
            Status = status
        });
        await context.SaveChangesAsync();
    }

    private async Task<List<MostBookedResourceDto>> RunMostBookedAsync(ReportGetDto dto)
    {
        await using var context = CreateContext();
        return await new ReportService(context).MostBookedResources(dto, CancellationToken.None);
    }

    private async Task<AverageBookingDurationDto> RunAverageAsync(ReportGetDto dto)
    {
        await using var context = CreateContext();
        return await new ReportService(context).AverageBookingDuration(dto, CancellationToken.None);
    }

    private async Task<List<BookingsPerDayDto>> RunPerDayAsync(ReportGetDto dto)
    {
        await using var context = CreateContext();
        return await new ReportService(context).BookingsPerDay(dto, CancellationToken.None);
    }

    // =====================================================================================
    // MostBookedResources
    // =====================================================================================

    [Fact]
    public async Task MostBooked_NoBookingsInWindow_ReturnsEmptyList()
    {
        var year = NextYear();
        await SeedResourceAsync();

        var result = await RunMostBookedAsync(Window(Utc(year, 3, 10), Utc(year, 3, 11)));

        Assert.Empty(result);
    }

    [Fact]
    public async Task MostBooked_OrdersByBookingCount_EvenWhenAnotherResourceHasMoreHours()
    {
        var year = NextYear();
        var user = await SeedUserAsync();
        var popular = await SeedResourceAsync();   // 3 short bookings = 3h
        var longer = await SeedResourceAsync();    // 2 long bookings  = 8h
        var single = await SeedResourceAsync();    // 1 very long booking = 10h
        var start = Utc(year, 3, 10, 8);
        for (var i = 0; i < 3; i++) await SeedBookingAsync(popular, user, start.AddHours(i * 3), 60);
        for (var i = 0; i < 2; i++) await SeedBookingAsync(longer, user, start.AddHours(i * 5), 240);
        await SeedBookingAsync(single, user, start, 600);

        var result = await RunMostBookedAsync(Window(Utc(year, 3, 10), Utc(year, 3, 11)));

        Assert.Equal(new[] { popular, longer, single }, result.Select(r => r.ResourceId).ToArray());
        Assert.Equal(new[] { 3, 2, 1 }, result.Select(r => r.BookingCount).ToArray());
        Assert.Equal(3.0, result[0].TotalBookedHours, 2);
        Assert.Equal(8.0, result[1].TotalBookedHours, 2);
        Assert.Equal(10.0, result[2].TotalBookedHours, 2);
    }

    [Fact]
    public async Task MostBooked_TieOnCount_OrdersByTotalHoursDescending()
    {
        var year = NextYear();
        var user = await SeedUserAsync();
        var shortBookings = await SeedResourceAsync();   // created first => lower id
        var longBookings = await SeedResourceAsync();
        var start = Utc(year, 3, 10, 8);
        for (var i = 0; i < 2; i++) await SeedBookingAsync(shortBookings, user, start.AddHours(i * 4), 60);
        for (var i = 0; i < 2; i++) await SeedBookingAsync(longBookings, user, start.AddHours(i * 4), 180);

        var result = await RunMostBookedAsync(Window(Utc(year, 3, 10), Utc(year, 3, 11)));

        Assert.Equal(new[] { longBookings, shortBookings }, result.Select(r => r.ResourceId).ToArray());
    }

    [Fact]
    public async Task MostBooked_TieOnCountAndHours_OrdersByResourceIdAscending()
    {
        var year = NextYear();
        var user = await SeedUserAsync();
        var first = await SeedResourceAsync();
        var second = await SeedResourceAsync();
        var start = Utc(year, 3, 10, 8);
        // Seed the later-created resource's bookings first, so insertion order cannot explain the result.
        for (var i = 0; i < 2; i++) await SeedBookingAsync(second, user, start.AddHours(i * 2), 60);
        for (var i = 0; i < 2; i++) await SeedBookingAsync(first, user, start.AddHours(i * 2), 60);

        var result = await RunMostBookedAsync(Window(Utc(year, 3, 10), Utc(year, 3, 11)));

        Assert.True(first < second);
        Assert.Equal(new[] { first, second }, result.Select(r => r.ResourceId).ToArray());
    }

    [Fact]
    public async Task MostBooked_ExcludesCancelledBookings()
    {
        var year = NextYear();
        var user = await SeedUserAsync();
        var mixed = await SeedResourceAsync();
        var onlyCancelled = await SeedResourceAsync();
        var start = Utc(year, 3, 10, 8);
        await SeedBookingAsync(mixed, user, start, 60);
        await SeedBookingAsync(mixed, user, start.AddHours(2), 60);
        for (var i = 0; i < 3; i++)
            await SeedBookingAsync(mixed, user, start.AddHours(4 + i), 60, BookingStatus.Cancelled);
        await SeedBookingAsync(onlyCancelled, user, start, 60, BookingStatus.Cancelled);

        var result = await RunMostBookedAsync(Window(Utc(year, 3, 10), Utc(year, 3, 11)));

        var row = Assert.Single(result);          // the cancelled-only resource does not appear at all
        Assert.Equal(mixed, row.ResourceId);
        Assert.Equal(2, row.BookingCount);
        Assert.Equal(2.0, row.TotalBookedHours, 2);
    }

    [Fact]
    public async Task MostBooked_WindowIsHalfOpenOnStartTime()
    {
        var year = NextYear();
        var user = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        await SeedBookingAsync(resource, user, Utc(year, 3, 9, 23, 59), 30);    // 1 minute before From: out
        await SeedBookingAsync(resource, user, Utc(year, 3, 10, 0, 0), 30);     // exactly From: in
        await SeedBookingAsync(resource, user, Utc(year, 3, 10, 23, 59), 30);   // 1 minute before To: in
        await SeedBookingAsync(resource, user, Utc(year, 3, 11, 0, 0), 30);     // exactly To: out

        var result = await RunMostBookedAsync(Window(Utc(year, 3, 10), Utc(year, 3, 11)));

        Assert.Equal(2, Assert.Single(result).BookingCount);
    }

    [Fact]
    public async Task MostBooked_BookingStartingInsideWindowButEndingAfterIt_IsCounted_WithItsFullDuration()
    {
        // The window is about when a booking STARTS. An EndTime-based filter would drop this row.
        var year = NextYear();
        var user = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        await SeedBookingAsync(resource, user, Utc(year, 3, 10, 23, 30), 120);

        var result = await RunMostBookedAsync(Window(Utc(year, 3, 10), Utc(year, 3, 11)));

        var row = Assert.Single(result);
        Assert.Equal(1, row.BookingCount);
        Assert.Equal(2.0, row.TotalBookedHours, 2);
    }

    [Fact]
    public async Task MostBooked_Top_IsAppliedAfterOrdering_NotBefore()
    {
        // Resources are created so the LOWEST ids have the FEWEST bookings: a Take before the ordering would
        // return the wrong resources.
        var year = NextYear();
        var user = await SeedUserAsync();
        var one = await SeedResourceAsync();
        var two = await SeedResourceAsync();
        var three = await SeedResourceAsync();
        var four = await SeedResourceAsync();
        var start = Utc(year, 3, 10, 6);
        var resources = new[] { one, two, three, four };
        for (var r = 0; r < resources.Length; r++)
            for (var i = 0; i <= r; i++)
                await SeedBookingAsync(resources[r], user, start.AddHours(i * 2), 60);

        var result = await RunMostBookedAsync(Window(Utc(year, 3, 10), Utc(year, 3, 11), top: 2));

        Assert.Equal(new[] { four, three }, result.Select(r => r.ResourceId).ToArray());
        Assert.Equal(new[] { 4, 3 }, result.Select(r => r.BookingCount).ToArray());
    }

    [Fact]
    public async Task MostBooked_TopNull_DefaultsToTen_AndKeepsTheTiebreakOrder()
    {
        var year = NextYear();
        var user = await SeedUserAsync();
        var ids = new List<int>();
        for (var i = 0; i < 12; i++)
        {
            var id = await SeedResourceAsync();
            ids.Add(id);
            await SeedBookingAsync(id, user, Utc(year, 3, 10, 8), 60);
        }

        var result = await RunMostBookedAsync(Window(Utc(year, 3, 10), Utc(year, 3, 11), top: null));

        Assert.Equal(10, result.Count);
        Assert.Equal(ids.Take(10).ToArray(), result.Select(r => r.ResourceId).ToArray());   // all tied => lowest ids
    }

    [Fact]
    public async Task MostBooked_InactiveResource_StillAppears_FlaggedInactive()
    {
        var year = NextYear();
        var user = await SeedUserAsync();
        var retired = await SeedResourceAsync(isActive: false);
        await SeedBookingAsync(retired, user, Utc(year, 3, 10, 8), 60);

        var result = await RunMostBookedAsync(Window(Utc(year, 3, 10), Utc(year, 3, 11)));

        var row = Assert.Single(result);
        Assert.Equal(retired, row.ResourceId);
        Assert.False(row.IsActive);
    }

    [Fact]
    public async Task MostBooked_ReturnsTheResourceName_AndRoundsHoursToTwoDecimals()
    {
        var year = NextYear();
        var user = await SeedUserAsync();
        var name = $"Boardroom_{Guid.NewGuid():N}";
        var resource = await SeedResourceAsync(name);
        await SeedBookingAsync(resource, user, Utc(year, 3, 10, 8), 20);   // 0.3333... hours

        var result = await RunMostBookedAsync(Window(Utc(year, 3, 10), Utc(year, 3, 11)));

        var row = Assert.Single(result);
        Assert.Equal(name, row.Name);
        Assert.True(row.IsActive);
        Assert.Equal(0.33, row.TotalBookedHours, 2);
        Assert.Equal(0.33, Math.Round(row.TotalBookedHours, 2));   // already rounded, not just close
    }

    // =====================================================================================
    // AverageBookingDuration
    // =====================================================================================

    [Fact]
    public async Task Average_NoBookingsInWindow_ReturnsZeroCountAndNullAverage()
    {
        var year = NextYear();

        var result = await RunAverageAsync(Window(Utc(year, 3, 10), Utc(year, 3, 11)));

        Assert.Equal(0, result.BookingCount);
        Assert.Null(result.AverageMinutes);
    }

    [Fact]
    public async Task Average_IsComputedAcrossAllBookings_NotPerBooking()
    {
        var year = NextYear();
        var user = await SeedUserAsync();
        var a = await SeedResourceAsync();
        var b = await SeedResourceAsync();
        await SeedBookingAsync(a, user, Utc(year, 3, 10, 8), 30);
        await SeedBookingAsync(a, user, Utc(year, 3, 10, 10), 60);
        await SeedBookingAsync(b, user, Utc(year, 3, 10, 12), 90);

        var result = await RunAverageAsync(Window(Utc(year, 3, 10), Utc(year, 3, 11)));

        Assert.Equal(3, result.BookingCount);
        Assert.Equal(60m, result.AverageMinutes);
    }

    [Fact]
    public async Task Average_IsRoundedToTwoDecimals()
    {
        var year = NextYear();
        var user = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        await SeedBookingAsync(resource, user, Utc(year, 3, 10, 8), 20);
        await SeedBookingAsync(resource, user, Utc(year, 3, 10, 10), 20);
        await SeedBookingAsync(resource, user, Utc(year, 3, 10, 12), 25);   // 65 / 3 = 21.6666...

        var result = await RunAverageAsync(Window(Utc(year, 3, 10), Utc(year, 3, 11)));

        Assert.Equal(3, result.BookingCount);
        Assert.Equal(21.67m, result.AverageMinutes);
    }

    [Fact]
    public async Task Average_ExcludesCancelledBookings()
    {
        var year = NextYear();
        var user = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        await SeedBookingAsync(resource, user, Utc(year, 3, 10, 8), 60);
        await SeedBookingAsync(resource, user, Utc(year, 3, 10, 12), 600, BookingStatus.Cancelled);

        var result = await RunAverageAsync(Window(Utc(year, 3, 10), Utc(year, 3, 11)));

        Assert.Equal(1, result.BookingCount);
        Assert.Equal(60m, result.AverageMinutes);
    }

    [Fact]
    public async Task Average_UsesTheSameHalfOpenStartTimeWindowAsTheOtherReports()
    {
        var year = NextYear();
        var user = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        await SeedBookingAsync(resource, user, Utc(year, 3, 9, 23, 59), 600);   // before From: out
        await SeedBookingAsync(resource, user, Utc(year, 3, 10, 0, 0), 30);     // exactly From: in
        await SeedBookingAsync(resource, user, Utc(year, 3, 10, 23, 50), 90);   // starts inside, ends after To: in
        await SeedBookingAsync(resource, user, Utc(year, 3, 11, 0, 0), 600);    // exactly To: out

        var result = await RunAverageAsync(Window(Utc(year, 3, 10), Utc(year, 3, 11)));

        Assert.Equal(2, result.BookingCount);
        Assert.Equal(60m, result.AverageMinutes);
    }

    // =====================================================================================
    // BookingsPerDay
    // =====================================================================================

    [Fact]
    public async Task PerDay_ReturnsOneRowPerDay_WithZeroForDaysWithoutBookings()
    {
        var year = NextYear();
        var user = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        await SeedBookingAsync(resource, user, Utc(year, 3, 10, 8), 60);
        await SeedBookingAsync(resource, user, Utc(year, 3, 10, 9), 60);
        await SeedBookingAsync(resource, user, Utc(year, 3, 13, 8), 60);

        var result = await RunPerDayAsync(Window(Utc(year, 3, 10), Utc(year, 3, 15)));

        Assert.Equal(
            new[] { Day(year, 3, 10), Day(year, 3, 11), Day(year, 3, 12), Day(year, 3, 13), Day(year, 3, 14) },
            result.Select(r => r.Date).ToArray());
        Assert.Equal(new[] { 2, 0, 0, 1, 0 }, result.Select(r => r.Count).ToArray());
    }

    [Fact]
    public async Task PerDay_GroupsBookingsByDay_NotByExactTimestamp()
    {
        var year = NextYear();
        var user = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        await SeedBookingAsync(resource, user, Utc(year, 3, 10, 8), 30);
        await SeedBookingAsync(resource, user, Utc(year, 3, 10, 10), 30);
        await SeedBookingAsync(resource, user, Utc(year, 3, 10, 23, 59), 30);

        var result = await RunPerDayAsync(Window(Utc(year, 3, 10), Utc(year, 3, 11)));

        var row = Assert.Single(result);
        Assert.Equal(Day(year, 3, 10), row.Date);
        Assert.Equal(3, row.Count);
    }

    [Fact]
    public async Task PerDay_ToExactlyAtMidnight_DoesNotAddAnExtraDay()
    {
        var year = NextYear();

        var result = await RunPerDayAsync(Window(Utc(year, 3, 10), Utc(year, 3, 13)));

        Assert.Equal(3, result.Count);
        Assert.Equal(Day(year, 3, 10), result.First().Date);
        Assert.Equal(Day(year, 3, 12), result.Last().Date);   // the 13th is outside the window
    }

    [Fact]
    public async Task PerDay_ToPartwayThroughADay_IncludesThatPartialDay()
    {
        var year = NextYear();
        var user = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        await SeedBookingAsync(resource, user, Utc(year, 3, 10, 9), 30);
        await SeedBookingAsync(resource, user, Utc(year, 3, 12, 4), 30);   // before To (05:00): in
        await SeedBookingAsync(resource, user, Utc(year, 3, 12, 6), 30);   // after To: out

        var result = await RunPerDayAsync(Window(Utc(year, 3, 10), Utc(year, 3, 12, 5)));

        Assert.Equal(
            new[] { Day(year, 3, 10), Day(year, 3, 11), Day(year, 3, 12) },
            result.Select(r => r.Date).ToArray());
        Assert.Equal(new[] { 1, 0, 1 }, result.Select(r => r.Count).ToArray());
    }

    [Fact]
    public async Task PerDay_FromPartwayThroughADay_FirstDayIsPartial()
    {
        var year = NextYear();
        var user = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        await SeedBookingAsync(resource, user, Utc(year, 3, 10, 9), 30);    // before From (10:00): out
        await SeedBookingAsync(resource, user, Utc(year, 3, 10, 11), 30);   // in
        await SeedBookingAsync(resource, user, Utc(year, 3, 11, 12), 30);   // in

        var result = await RunPerDayAsync(Window(Utc(year, 3, 10, 10), Utc(year, 3, 12)));

        Assert.Equal(new[] { Day(year, 3, 10), Day(year, 3, 11) }, result.Select(r => r.Date).ToArray());
        Assert.Equal(new[] { 1, 1 }, result.Select(r => r.Count).ToArray());
    }

    [Fact]
    public async Task PerDay_LateEveningAndMidnightBookings_LandOnTheirOwnUtcDay()
    {
        var year = NextYear();
        var user = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        await SeedBookingAsync(resource, user, Utc(year, 3, 10, 23, 30), 30);   // still the 10th
        await SeedBookingAsync(resource, user, Utc(year, 3, 11, 0, 0), 30);     // exactly midnight => the 11th

        var result = await RunPerDayAsync(Window(Utc(year, 3, 10), Utc(year, 3, 12)));

        Assert.Equal(new[] { 1, 1 }, result.Select(r => r.Count).ToArray());
    }

    [Fact]
    public async Task PerDay_ExcludesCancelledBookings()
    {
        var year = NextYear();
        var user = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        await SeedBookingAsync(resource, user, Utc(year, 3, 10, 8), 30);
        await SeedBookingAsync(resource, user, Utc(year, 3, 10, 10), 30, BookingStatus.Cancelled);

        var result = await RunPerDayAsync(Window(Utc(year, 3, 10), Utc(year, 3, 11)));

        Assert.Equal(1, Assert.Single(result).Count);
    }

    [Fact]
    public async Task PerDay_CountsBookingsAcrossAllResources()
    {
        var year = NextYear();
        var user = await SeedUserAsync();
        var a = await SeedResourceAsync();
        var b = await SeedResourceAsync();
        await SeedBookingAsync(a, user, Utc(year, 3, 10, 8), 30);
        await SeedBookingAsync(b, user, Utc(year, 3, 10, 8), 30);

        var result = await RunPerDayAsync(Window(Utc(year, 3, 10), Utc(year, 3, 11)));

        Assert.Equal(2, Assert.Single(result).Count);
    }

    [Fact]
    public async Task PerDay_RangeGivenWithANonUtcOffset_IsInterpretedInUtc()
    {
        // 2026-style client sending local-time bounds: 00:00 at +05:30 is 18:30 UTC the previous day. Days must be
        // UTC days, and a booking in that "extra" evening must not be silently dropped.
        var year = NextYear();
        var user = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        var from = new DateTimeOffset(year, 3, 10, 0, 0, 0, TimeSpan.FromHours(5.5));   // Mar 9 18:30 UTC
        var to = from.AddDays(2);                                                         // Mar 11 18:30 UTC
        await SeedBookingAsync(resource, user, Utc(year, 3, 9, 20), 30);    // inside (after 18:30 UTC)
        await SeedBookingAsync(resource, user, Utc(year, 3, 11, 10), 30);   // inside
        await SeedBookingAsync(resource, user, Utc(year, 3, 11, 19), 30);   // after To: out

        var result = await RunPerDayAsync(new ReportGetDto(From: from, To: to, Top: null));

        Assert.Equal(
            new[] { Day(year, 3, 9), Day(year, 3, 10), Day(year, 3, 11) },
            result.Select(r => r.Date).ToArray());
        Assert.Equal(new[] { 1, 0, 1 }, result.Select(r => r.Count).ToArray());
    }
}