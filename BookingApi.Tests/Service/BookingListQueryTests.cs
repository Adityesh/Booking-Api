using BookingApi.Data;
using BookingApi.Dto;
using BookingApi.Dto.Booking;
using BookingApi.Service;
using BookingApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Tests.Service;

// GetMyBookingsAsync and GetAdminBookingsAsync against real Postgres.
// The bookings table is shared with every other class in the collection, so each test isolates its rows:
// "my" queries are naturally scoped to a fresh user; admin queries filter by a fresh resource or user, and the
// date-range tests use a per-test year (1700+) that no other suite seeds bookings in.
[Collection("Postgres")]
public class BookingListQueryTests(PostgresFixture fixture)
{
    private static int _yearCounter;

    private readonly DateTime _yearStart =
        new(1700 + Interlocked.Increment(ref _yearCounter), 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Future slots, relative to the real clock: GetMyBookingsAsync reads DateTime.UtcNow itself.
    private static DateTime Future(int hours) => DateTime.UtcNow.Date.AddDays(3).AddHours(hours);

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options);
    }

    // ---------- seeding ----------

    private async Task<(int Id, string Username)> SeedUserAsync()
    {
        await using var context = CreateContext();
        var user = new UserEntity
        {
            Username = $"u_{Guid.NewGuid():N}",
            PasswordHash = "not-a-real-hash",
            Role = UserRole.User,
            IsActive = true
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return (user.Id, user.Username);
    }

    private async Task<(int Id, string Name)> SeedResourceAsync(bool isActive = true)
    {
        await using var context = CreateContext();
        var resource = new ResourceEntity
        {
            Name = $"resource_{Guid.NewGuid():N}",
            Type = ResourceType.Room,
            Capacity = 10,
            IsActive = isActive
        };
        context.Resources.Add(resource);
        await context.SaveChangesAsync();
        return (resource.Id, resource.Name);
    }

    private async Task<int> SeedBookingAsync(int resourceId, int userId, DateTime start, DateTime? end = null,
        BookingStatus status = BookingStatus.Confirmed)
    {
        await using var context = CreateContext();
        var booking = new BookingEntity
        {
            ResourceId = resourceId,
            UserId = userId,
            StartTime = start,
            EndTime = end ?? start.AddHours(1),
            Status = status
        };
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();
        return booking.Id;
    }

    // ---------- the calls under test ----------

    private async Task<PagedResult<BookingListItemDto>> MyAsync(int userId, BookingTimeFrame? when = null,
        BookingStatus? status = null, int page = 1, int pageSize = 20)
    {
        await using var context = CreateContext();
        var service = new BookingService(context, new AuditLogService(context));
        return await service.GetMyBookingsAsync(
            new GetMyBookingDto(Status: status, When: when, Page: page, PageSize: pageSize), userId,
            CancellationToken.None);
    }

    private async Task<PagedResult<BookingListItemDto>> AdminAsync(int? userId = null, int? resourceId = null,
        BookingStatus? status = null, DateTimeOffset? from = null, DateTimeOffset? to = null, int page = 1,
        int pageSize = 20)
    {
        await using var context = CreateContext();
        var service = new BookingService(context, new AuditLogService(context));
        return await service.GetAdminBookingsAsync(
            new GetAdminBookingDto(UserId: userId, ResourceId: resourceId, Status: status, From: from, To: to,
                Page: page, PageSize: pageSize),
            CancellationToken.None);
    }

    private static int[] Ids(PagedResult<BookingListItemDto> result) => result.Data.Select(b => b.Id).ToArray();

    // ======================= my bookings: ownership =======================

    [Fact]
    public async Task My_ReturnsOnlyTheCallersBookings()
    {
        var me = await SeedUserAsync();
        var other = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        var mine = await SeedBookingAsync(resource.Id, me.Id, Future(10));
        await SeedBookingAsync(resource.Id, other.Id, Future(10));

        var result = await MyAsync(me.Id);

        Assert.Equal([mine], Ids(result));
        Assert.Equal(1, result.Count);
    }

    [Fact]
    public async Task My_NoBookings_ReturnsAnEmptyPage()
    {
        var me = await SeedUserAsync();

        var result = await MyAsync(me.Id);

        Assert.Empty(result.Data);
        Assert.Equal(0, result.Count);
    }

    // ======================= my bookings: Upcoming / Past / all =======================

    [Fact]
    public async Task My_Upcoming_IncludesInProgressAndFuture_SoonestFirst_ExcludesFinished()
    {
        var me = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        var now = DateTime.UtcNow;
        var finished = await SeedBookingAsync(resource.Id, me.Id, now.AddHours(-3), now.AddHours(-2));
        var later = await SeedBookingAsync(resource.Id, me.Id, now.AddDays(2));
        var inProgress = await SeedBookingAsync(resource.Id, me.Id, now.AddMinutes(-30), now.AddMinutes(30));
        var sooner = await SeedBookingAsync(resource.Id, me.Id, now.AddDays(1));

        var result = await MyAsync(me.Id, BookingTimeFrame.Upcoming);

        Assert.Equal([inProgress, sooner, later], Ids(result));
        Assert.Equal(3, result.Count);
        Assert.DoesNotContain(finished, Ids(result));
    }

    [Fact]
    public async Task My_Past_IncludesOnlyFinished_MostRecentFirst()
    {
        var me = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        var now = DateTime.UtcNow;
        var older = await SeedBookingAsync(resource.Id, me.Id, now.AddDays(-5));
        var justEnded = await SeedBookingAsync(resource.Id, me.Id, now.AddHours(-1).AddMinutes(-1));   // ended 1 min ago
        await SeedBookingAsync(resource.Id, me.Id, now.AddMinutes(-30), now.AddMinutes(30));          // in progress
        await SeedBookingAsync(resource.Id, me.Id, now.AddDays(1));                                   // future

        var result = await MyAsync(me.Id, BookingTimeFrame.Past);

        Assert.Equal([justEnded, older], Ids(result));
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task My_UpcomingAndPast_TogetherCoverEveryBooking_ExactlyOnce()
    {
        // EndTime > now vs EndTime <= now: complementary, so no booking is missing from both tabs or shown in both.
        var me = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        var now = DateTime.UtcNow;
        int[] all =
        [
            await SeedBookingAsync(resource.Id, me.Id, now.AddDays(-2)),
            await SeedBookingAsync(resource.Id, me.Id, now.AddMinutes(-30), now.AddMinutes(30)),
            await SeedBookingAsync(resource.Id, me.Id, now.AddDays(2))
        ];

        var upcoming = Ids(await MyAsync(me.Id, BookingTimeFrame.Upcoming));
        var past = Ids(await MyAsync(me.Id, BookingTimeFrame.Past));

        Assert.Empty(upcoming.Intersect(past));
        Assert.Equal(all.Order(), upcoming.Concat(past).Order());
    }

    [Fact]
    public async Task My_NoTimeFrame_ReturnsPastAndUpcoming_NewestFirst()
    {
        var me = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        var now = DateTime.UtcNow;
        var past = await SeedBookingAsync(resource.Id, me.Id, now.AddDays(-2));
        var future = await SeedBookingAsync(resource.Id, me.Id, now.AddDays(2));

        var result = await MyAsync(me.Id, when: null);

        Assert.Equal([future, past], Ids(result));
        Assert.Equal(2, result.Count);
    }

    // ======================= my bookings: status, paging, ties =======================

    [Fact]
    public async Task My_StatusFilter_ReturnsOnlyThatStatus_AndCountsOnlyThose()
    {
        var me = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        var confirmed = await SeedBookingAsync(resource.Id, me.Id, Future(10));
        var cancelled = await SeedBookingAsync(resource.Id, me.Id, Future(12), status: BookingStatus.Cancelled);

        var onlyCancelled = await MyAsync(me.Id, status: BookingStatus.Cancelled);
        var onlyConfirmed = await MyAsync(me.Id, status: BookingStatus.Confirmed);

        Assert.Equal([cancelled], Ids(onlyCancelled));
        Assert.Equal(1, onlyCancelled.Count);
        Assert.Equal([confirmed], Ids(onlyConfirmed));
    }

    [Theory]
    [InlineData(BookingTimeFrame.Upcoming)]
    [InlineData(BookingTimeFrame.Past)]
    [InlineData(null)]
    public async Task My_Paging_WalksEveryBookingExactlyOnce_AndCountIsTheTotal(BookingTimeFrame? when)
    {
        // Five distinct start times; page size 2 → pages of 2, 2, 1. Skip-before-Take is what makes page 2 non-empty.
        var me = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        var sign = when == BookingTimeFrame.Past ? -1 : 1;
        var seeded = new List<int>();
        for (var i = 1; i <= 5; i++)
            seeded.Add(await SeedBookingAsync(resource.Id, me.Id, DateTime.UtcNow.AddDays(sign * (i + 1))));

        var pages = new List<PagedResult<BookingListItemDto>>();
        for (var page = 1; page <= 4; page++)
            pages.Add(await MyAsync(me.Id, when, page: page, pageSize: 2));

        Assert.Equal([2, 2, 1, 0], pages.Select(p => p.Data.Count));
        Assert.All(pages, p => Assert.Equal(5, p.Count));   // Count is the total before paging, on every page
        Assert.Equal(seeded.Order(), pages.SelectMany(p => p.Data).Select(b => b.Id).Order());
    }

    [Theory]
    [InlineData(BookingTimeFrame.Upcoming)]
    [InlineData(BookingTimeFrame.Past)]
    [InlineData(null)]
    public async Task My_TiedStartTimes_AreOrderedById_SoPagesNeverOverlap(BookingTimeFrame? when)
    {
        // Identical slots are normal (capacity > 1). Without an Id tiebreak Postgres may order ties differently
        // per query, and a row can show on two pages or on none.
        var me = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        var start = when == BookingTimeFrame.Past ? DateTime.UtcNow.AddDays(-2) : DateTime.UtcNow.AddDays(2);
        var seeded = new List<int>();
        for (var i = 0; i < 5; i++)
            seeded.Add(await SeedBookingAsync(resource.Id, me.Id, start));

        var walked = new List<int>();
        for (var page = 1; page <= 3; page++)
            walked.AddRange(Ids(await MyAsync(me.Id, when, page: page, pageSize: 2)));

        // Upcoming reads oldest-first (ascending Id); Past and "all" read newest-first (descending Id).
        var expected = when == BookingTimeFrame.Upcoming ? seeded.Order() : seeded.OrderDescending();
        Assert.Equal(expected, walked);
    }

    // ======================= my bookings: projection =======================

    [Fact]
    public async Task My_Items_CarryTheResourceNameAndUsername_EvenForAnInactiveResource()
    {
        // The list must not need a per-row resource lookup, which 404s for a non-admin once the resource is retired.
        var me = await SeedUserAsync();
        var retired = await SeedResourceAsync(isActive: false);
        var start = Future(10);
        var id = await SeedBookingAsync(retired.Id, me.Id, start, status: BookingStatus.Cancelled);

        var item = Assert.Single((await MyAsync(me.Id)).Data);

        Assert.Equal(id, item.Id);
        Assert.Equal(retired.Id, item.ResourceId);
        Assert.Equal(retired.Name, item.ResourceName);
        Assert.Equal(me.Id, item.UserId);
        Assert.Equal(me.Username, item.Username);
        Assert.Equal(BookingStatus.Cancelled, item.Status);
        Assert.Equal(start, item.StartTime, TimeSpan.FromMilliseconds(1));
        Assert.Equal(DateTimeKind.Utc, item.StartTime.Kind);   // serialised with a Z, which the frontend relies on
    }

    // ======================= admin: filters =======================

    [Fact]
    public async Task Admin_UserFilter_ReturnsOnlyThatUsersBookings()
    {
        var a = await SeedUserAsync();
        var b = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        var aBooking = await SeedBookingAsync(resource.Id, a.Id, Future(10));
        await SeedBookingAsync(resource.Id, b.Id, Future(10));

        var result = await AdminAsync(userId: a.Id);

        Assert.Equal([aBooking], Ids(result));
        Assert.Equal(1, result.Count);
    }

    [Fact]
    public async Task Admin_ResourceFilter_ReturnsEveryUsersBookingsOnThatResource_Only()
    {
        var a = await SeedUserAsync();
        var b = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        var otherResource = await SeedResourceAsync();
        var aBooking = await SeedBookingAsync(resource.Id, a.Id, Future(10));
        var bBooking = await SeedBookingAsync(resource.Id, b.Id, Future(12));
        await SeedBookingAsync(otherResource.Id, a.Id, Future(10));

        var result = await AdminAsync(resourceId: resource.Id);

        Assert.Equal([bBooking, aBooking], Ids(result));   // newest first
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Admin_StatusFilter_CombinesWithTheOtherFilters()
    {
        var user = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        await SeedBookingAsync(resource.Id, user.Id, Future(10));
        var cancelled = await SeedBookingAsync(resource.Id, user.Id, Future(12), status: BookingStatus.Cancelled);

        var result = await AdminAsync(resourceId: resource.Id, status: BookingStatus.Cancelled);

        Assert.Equal([cancelled], Ids(result));
        Assert.Equal(1, result.Count);
    }

    [Fact]
    public async Task Admin_DateBounds_AreInclusiveAtFrom_AndExclusiveAtTo_OnStartTime()
    {
        var user = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        var from = _yearStart;
        var to = _yearStart.AddDays(1);
        await SeedBookingAsync(resource.Id, user.Id, from.AddSeconds(-1));
        var atFrom = await SeedBookingAsync(resource.Id, user.Id, from);
        var startsInside = await SeedBookingAsync(resource.Id, user.Id, to.AddMinutes(-30));   // ends after To: still counts
        await SeedBookingAsync(resource.Id, user.Id, to);

        // No resource/user filter: the per-test year is what isolates this window.
        var result = await AdminAsync(from: new DateTimeOffset(from), to: new DateTimeOffset(to));

        Assert.Equal([startsInside, atFrom], Ids(result));
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Admin_BoundsGivenInAnotherTimeZone_AreComparedAsTheSameInstant()
    {
        var user = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        var start = _yearStart.AddHours(5);
        var id = await SeedBookingAsync(resource.Id, user.Id, start);
        var sameInstantInIndia = new DateTimeOffset(start).ToOffset(TimeSpan.FromHours(5.5));

        var included = await AdminAsync(resourceId: resource.Id, from: sameInstantInIndia);
        var excluded = await AdminAsync(resourceId: resource.Id, from: sameInstantInIndia.AddSeconds(1));

        Assert.Equal([id], Ids(included));
        Assert.Empty(excluded.Data);
    }

    // ======================= admin: ordering and paging =======================

    [Fact]
    public async Task Admin_Paging_WalksEveryBookingExactlyOnce_NewestFirst_WithIdTiebreak()
    {
        var user = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        var early = await SeedBookingAsync(resource.Id, user.Id, Future(8));
        var tiedFirst = await SeedBookingAsync(resource.Id, user.Id, Future(10));
        var tiedSecond = await SeedBookingAsync(resource.Id, user.Id, Future(10));
        var late = await SeedBookingAsync(resource.Id, user.Id, Future(12));
        var tiedThird = await SeedBookingAsync(resource.Id, user.Id, Future(10));

        var pages = new List<PagedResult<BookingListItemDto>>();
        for (var page = 1; page <= 3; page++)
            pages.Add(await AdminAsync(resourceId: resource.Id, page: page, pageSize: 2));

        Assert.Equal([late, tiedThird, tiedSecond, tiedFirst, early], pages.SelectMany(p => p.Data).Select(b => b.Id));
        Assert.All(pages, p => Assert.Equal(5, p.Count));
        Assert.All(pages, p => Assert.Equal(2, p.PageSize));
        Assert.Equal([1, 2, 3], pages.Select(p => p.Page));
    }

    [Fact]
    public async Task Admin_PageBeyondTheEnd_IsEmpty_ButStillReportsTheTotal()
    {
        var user = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        await SeedBookingAsync(resource.Id, user.Id, Future(10));

        var result = await AdminAsync(resourceId: resource.Id, page: 5, pageSize: 20);

        Assert.Empty(result.Data);
        Assert.Equal(1, result.Count);
    }

    [Fact]
    public async Task Admin_Items_CarryTheBookingOwnersUsername()
    {
        var owner = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        await SeedBookingAsync(resource.Id, owner.Id, Future(10));

        var item = Assert.Single((await AdminAsync(resourceId: resource.Id)).Data);

        Assert.Equal(owner.Id, item.UserId);
        Assert.Equal(owner.Username, item.Username);
        Assert.Equal(resource.Name, item.ResourceName);
    }
}
