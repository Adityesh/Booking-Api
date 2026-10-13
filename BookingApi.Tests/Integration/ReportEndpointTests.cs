using System.Globalization;
using System.Net;
using System.Text.Json;
using BookingApi.Data;
using BookingApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Tests.Integration;

// The real pipeline over the shared Postgres: [Authorize], [FromQuery] binding, DI registration, validation, the
// EF-to-SQL translation of all three queries, and JSON shapes. Bookings are seeded in a calendar year of their own
// per test (1201, 1202, ...), the same trick as ReportServiceTests (which uses 1001+), so totals are exact.
[Collection("Postgres")]
public class ReportsEndpointTests(PostgresFixture fixture) : ApiTestBase(fixture)
{
    private const string Base = "/api/reports";
    private const string MostBooked = Base + "/most-booked-resources";
    private const string AverageDuration = Base + "/average-booking-duration";
    private const string PerDay = Base + "/bookings-per-day";

    private static int _yearCounter;
    private static int NextYear() => 1200 + Interlocked.Increment(ref _yearCounter);

    public static TheoryData<string> ReportRoutes => new() { MostBooked, AverageDuration, PerDay };

    // ---------- helpers ----------

    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    // Invariant culture: ':' in a custom format is the culture's time separator otherwise.
    private static string Iso(DateTime utc) =>
        utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Range(DateTime from, DateTime to) => $"from={Iso(from)}&to={Iso(to)}";

    private static string DateText(int year, int month, int day) =>
        new DateOnly(year, month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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

    private async Task<string> ResourceNameAsync(int resourceId)
    {
        await using var context = CreateContext();
        return await context.Resources.AsNoTracking().Where(r => r.Id == resourceId).Select(r => r.Name).SingleAsync();
    }

    // =====================================================================================
    // who may call what
    // =====================================================================================

    [Theory]
    [MemberData(nameof(ReportRoutes))]
    public async Task Anonymous_Gets401(string route)
    {
        using var client = CreateClient();

        var response = await client.GetAsync($"{route}?{Range(Utc(2000, 1, 1), Utc(2000, 1, 8))}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ReportRoutes))]
    public async Task NormalUser_Gets403(string route)
    {
        var user = await CreateUserWithTokenAsync();
        using var client = CreateClient(user.Token);

        // A VALID query, so the 403 can only be about the role.
        var response = await client.GetAsync($"{route}?{Range(Utc(2000, 1, 1), Utc(2000, 1, 8))}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =====================================================================================
    // validation through the real pipeline
    // =====================================================================================

    [Theory]
    [InlineData("")]                                                                       // nothing at all
    [InlineData("from=2000-01-01T00:00:00Z")]                                              // no "to"
    [InlineData("to=2000-01-08T00:00:00Z")]                                                // no "from"
    [InlineData("from=2000-01-08T00:00:00Z&to=2000-01-01T00:00:00Z")]                      // reversed
    [InlineData("from=2000-01-01T00:00:00Z&to=2000-01-01T00:00:00Z")]                      // empty range
    [InlineData("from=2000-01-01T00:00:00Z&to=2001-06-01T00:00:00Z")]                      // far over 366 days
    [InlineData("from=2000-01-01T00:00:00Z&to=2000-01-08T00:00:00Z&top=0")]
    [InlineData("from=2000-01-01T00:00:00Z&to=2000-01-08T00:00:00Z&top=51")]
    [InlineData("from=not-a-date&to=2000-01-08T00:00:00Z")]
    public async Task Admin_InvalidQuery_Returns400_OnEveryReport(string query)
    {
        var admin = await CreateUserWithTokenAsync(UserRole.Admin);
        using var client = CreateClient(admin.Token);

        foreach (var route in new[] { MostBooked, AverageDuration, PerDay })
        {
            var response = await client.GetAsync($"{route}?{query}");

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest,
                $"{route}?{query} returned {(int)response.StatusCode}");
        }
    }

    // =====================================================================================
    // end to end: seed, call as an admin, read the JSON
    // =====================================================================================

    [Fact]
    public async Task MostBookedResources_ReturnsTheRankedResources_ExcludingCancelled_AndHonouringTop()
    {
        var year = NextYear();
        var user = await SeedUserAsync();
        var busy = await SeedResourceAsync();
        var quiet = await SeedResourceAsync();
        var third = await SeedResourceAsync();
        await SeedBookingAsync(busy, user.Id, Utc(year, 3, 10, 8), 60);
        await SeedBookingAsync(busy, user.Id, Utc(year, 3, 10, 10), 120);
        await SeedBookingAsync(busy, user.Id, Utc(year, 3, 10, 14), 600, BookingStatus.Cancelled);
        await SeedBookingAsync(quiet, user.Id, Utc(year, 3, 10, 8), 30);
        await SeedBookingAsync(third, user.Id, Utc(year, 3, 10, 8), 30);
        var admin = await CreateUserWithTokenAsync(UserRole.Admin);
        using var client = CreateClient(admin.Token);

        var all = await client.GetAsync($"{MostBooked}?{Range(Utc(year, 3, 10), Utc(year, 3, 11))}");
        var topTwo = await client.GetAsync($"{MostBooked}?{Range(Utc(year, 3, 10), Utc(year, 3, 11))}&top=2");

        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        using (var doc = await ReadJsonAsync(all))
        {
            var rows = doc.RootElement;
            Assert.Equal(3, rows.GetArrayLength());
            Assert.Equal(busy, rows[0].GetProperty("resourceId").GetInt32());
            Assert.Equal(await ResourceNameAsync(busy), rows[0].GetProperty("name").GetString());
            Assert.True(rows[0].GetProperty("isActive").GetBoolean());
            Assert.Equal(2, rows[0].GetProperty("bookingCount").GetInt32());
            Assert.Equal(3.0, rows[0].GetProperty("totalBookedHours").GetDouble(), 2);
            Assert.Equal(quiet, rows[1].GetProperty("resourceId").GetInt32());   // tie with "third": lower id first
            Assert.Equal(third, rows[2].GetProperty("resourceId").GetInt32());
        }

        Assert.Equal(HttpStatusCode.OK, topTwo.StatusCode);
        using (var doc = await ReadJsonAsync(topTwo))
        {
            Assert.Equal(2, doc.RootElement.GetArrayLength());
            Assert.Equal(busy, doc.RootElement[0].GetProperty("resourceId").GetInt32());
            Assert.Equal(quiet, doc.RootElement[1].GetProperty("resourceId").GetInt32());
        }
    }

    [Fact]
    public async Task MostBookedResources_NoBookings_Returns200WithAnEmptyArray()
    {
        var year = NextYear();
        var admin = await CreateUserWithTokenAsync(UserRole.Admin);
        using var client = CreateClient(admin.Token);

        var response = await client.GetAsync($"{MostBooked}?{Range(Utc(year, 3, 10), Utc(year, 3, 11))}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task AverageBookingDuration_ReturnsCountAndAverage()
    {
        var year = NextYear();
        var user = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        await SeedBookingAsync(resource, user.Id, Utc(year, 3, 10, 8), 30);
        await SeedBookingAsync(resource, user.Id, Utc(year, 3, 10, 10), 90);
        await SeedBookingAsync(resource, user.Id, Utc(year, 3, 10, 12), 600, BookingStatus.Cancelled);
        var admin = await CreateUserWithTokenAsync(UserRole.Admin);
        using var client = CreateClient(admin.Token);

        var response = await client.GetAsync($"{AverageDuration}?{Range(Utc(year, 3, 10), Utc(year, 3, 11))}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await ReadJsonAsync(response);
        Assert.Equal(2, doc.RootElement.GetProperty("bookingCount").GetInt32());
        Assert.Equal(60m, doc.RootElement.GetProperty("averageMinutes").GetDecimal());
    }

    [Fact]
    public async Task AverageBookingDuration_NoBookings_Returns200_WithZeroCountAndNullAverage()
    {
        var year = NextYear();
        var admin = await CreateUserWithTokenAsync(UserRole.Admin);
        using var client = CreateClient(admin.Token);

        var response = await client.GetAsync($"{AverageDuration}?{Range(Utc(year, 3, 10), Utc(year, 3, 11))}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);   // not a 500 from averaging an empty set
        using var doc = await ReadJsonAsync(response);
        Assert.Equal(0, doc.RootElement.GetProperty("bookingCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("averageMinutes").ValueKind);
    }

    [Fact]
    public async Task BookingsPerDay_ReturnsOneRowPerDay_WithZeroFill_AndIsoDates()
    {
        var year = NextYear();
        var user = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        await SeedBookingAsync(resource, user.Id, Utc(year, 3, 10, 8), 30);
        await SeedBookingAsync(resource, user.Id, Utc(year, 3, 12, 8), 30);
        await SeedBookingAsync(resource, user.Id, Utc(year, 3, 12, 9), 30);
        var admin = await CreateUserWithTokenAsync(UserRole.Admin);
        using var client = CreateClient(admin.Token);

        var response = await client.GetAsync($"{PerDay}?{Range(Utc(year, 3, 10), Utc(year, 3, 13))}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await ReadJsonAsync(response);
        var rows = doc.RootElement;
        Assert.Equal(3, rows.GetArrayLength());
        Assert.Equal(DateText(year, 3, 10), rows[0].GetProperty("date").GetString());
        Assert.Equal(1, rows[0].GetProperty("count").GetInt32());
        Assert.Equal(DateText(year, 3, 11), rows[1].GetProperty("date").GetString());
        Assert.Equal(0, rows[1].GetProperty("count").GetInt32());
        Assert.Equal(DateText(year, 3, 12), rows[2].GetProperty("date").GetString());
        Assert.Equal(2, rows[2].GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task BookingsPerDay_AcceptsAnEncodedNonUtcOffset_AndTreatsItAsAnInstant()
    {
        // The '+' in "+05:30" must be sent as %2B; raw, it would arrive as a space and fail to bind.
        var year = NextYear();
        var user = await SeedUserAsync();
        var resource = await SeedResourceAsync();
        await SeedBookingAsync(resource, user.Id, Utc(year, 3, 9, 20), 30);    // inside: after 18:30 UTC on the 9th
        await SeedBookingAsync(resource, user.Id, Utc(year, 3, 11, 10), 30);   // inside
        var admin = await CreateUserWithTokenAsync(UserRole.Admin);
        using var client = CreateClient(admin.Token);
        var from = Uri.EscapeDataString($"{year:D4}-03-10T00:00:00+05:30");   // = Mar 9 18:30 UTC
        var to = Uri.EscapeDataString($"{year:D4}-03-12T00:00:00+05:30");     // = Mar 11 18:30 UTC

        var response = await client.GetAsync($"{PerDay}?from={from}&to={to}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await ReadJsonAsync(response);
        var rows = doc.RootElement;
        Assert.Equal(3, rows.GetArrayLength());                                           // UTC days: 9th, 10th, 11th
        Assert.Equal(DateText(year, 3, 9), rows[0].GetProperty("date").GetString());
        Assert.Equal(1, rows[0].GetProperty("count").GetInt32());
        Assert.Equal(0, rows[1].GetProperty("count").GetInt32());
        Assert.Equal(1, rows[2].GetProperty("count").GetInt32());
    }
}