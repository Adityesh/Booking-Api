using System.Net;
using System.Net.Http.Json;
using BookingApi.Data;
using BookingApi.Dto;
using BookingApi.Dto.Booking;
using BookingApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Tests.Integration;

// GET /api/booking/me and GET /api/booking through the real pipeline: routing, query-string binding (enums,
// offsets, record defaults), the 400 shape and what is actually on the wire. 401/403 live in
// AuthorizationIntegrationTests; the query logic itself in BookingListQueryTests.
[Collection("Postgres")]
public class BookingListEndpointTests(PostgresFixture fixture) : ApiTestBase(fixture)
{
    private static string? MediaType(HttpResponseMessage response) => response.Content.Headers.ContentType?.MediaType;

    private static async Task<PagedResult<BookingListItemDto>> ReadPageAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", MediaType(response));
        var page = await response.Content.ReadFromJsonAsync<PagedResult<BookingListItemDto>>(ApiJson);
        return page!;
    }

    private async Task SetStatusAsync(int bookingId, BookingStatus status)
    {
        await using var context = CreateContext();
        await context.Bookings.Where(b => b.Id == bookingId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, status));
    }

    // ======================= /me =======================

    [Fact]
    public async Task Me_ReturnsOnlyTheCallersBookings_WithTheDefaultPaging()
    {
        var owner = await CreateUserWithTokenAsync();
        var other = await CreateUserWithTokenAsync();
        var resourceId = await SeedResourceAsync(capacity: 5);
        var mine = await SeedBookingAsync(resourceId, owner.Id);
        await SeedBookingAsync(resourceId, other.Id);
        using var client = CreateClient(owner.Token);

        var page = await ReadPageAsync(await client.GetAsync($"{Routes.Booking}/me"));

        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(1, page.Count);
        var item = Assert.Single(page.Data);
        Assert.Equal(mine, item.Id);
        Assert.Equal(owner.Username, item.Username);
    }

    [Fact]
    public async Task Me_IgnoresAUserIdInTheQueryString()
    {
        // Ownership comes from the token only. GetMyBookingDto has no UserId, so this must bind to nothing.
        var owner = await CreateUserWithTokenAsync();
        var victim = await CreateUserWithTokenAsync();
        var resourceId = await SeedResourceAsync(capacity: 5);
        await SeedBookingAsync(resourceId, victim.Id);
        using var client = CreateClient(owner.Token);

        var page = await ReadPageAsync(await client.GetAsync($"{Routes.Booking}/me?userId={victim.Id}"));

        Assert.Empty(page.Data);
    }

    [Fact]
    public async Task Me_WhenAndStatus_BindFromTheQueryStringByName()
    {
        var owner = await CreateUserWithTokenAsync();
        var resourceId = await SeedResourceAsync(capacity: 5);
        var finished = await SeedBookingAsync(resourceId, owner.Id, DateTime.UtcNow.AddDays(-2));
        var upcoming = await SeedBookingAsync(resourceId, owner.Id);
        var cancelled = await SeedBookingAsync(resourceId, owner.Id, FutureSlot(hour: 14));
        await SetStatusAsync(cancelled, BookingStatus.Cancelled);
        using var client = CreateClient(owner.Token);

        var past = await ReadPageAsync(await client.GetAsync($"{Routes.Booking}/me?when=Past"));
        var upcomingConfirmed =
            await ReadPageAsync(await client.GetAsync($"{Routes.Booking}/me?when=Upcoming&status=Confirmed"));

        Assert.Equal([finished], past.Data.Select(b => b.Id));
        Assert.Equal([upcoming], upcomingConfirmed.Data.Select(b => b.Id));
    }

    [Theory]
    [InlineData("pageSize=101")]
    [InlineData("pageSize=0")]
    [InlineData("page=0")]
    [InlineData("when=Tomorrow")]
    [InlineData("when=7")]
    [InlineData("status=Pending")]
    public async Task Me_InvalidQuery_Returns400ProblemDetails(string query)
    {
        var user = await CreateUserWithTokenAsync();
        using var client = CreateClient(user.Token);

        var response = await client.GetAsync($"{Routes.Booking}/me?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", MediaType(response));
    }

    // ======================= admin list =======================

    [Fact]
    public async Task Admin_SeesEveryUsersBookings_OnAResource()
    {
        var a = await SeedUserAsync();
        var b = await SeedUserAsync();
        var resourceId = await SeedResourceAsync(capacity: 5);
        var aBooking = await SeedBookingAsync(resourceId, a.Id);
        var bBooking = await SeedBookingAsync(resourceId, b.Id);
        var admin = await CreateUserWithTokenAsync(UserRole.Admin);
        using var client = CreateClient(admin.Token);

        var page = await ReadPageAsync(await client.GetAsync($"{Routes.Booking}?resourceId={resourceId}"));

        Assert.Equal(2, page.Count);
        Assert.Equal([aBooking, bBooking], page.Data.Select(x => x.Id).Order());
        Assert.Equal([a.Username, b.Username], page.Data.OrderBy(x => x.Id).Select(x => x.Username));
    }

    [Fact]
    public async Task Admin_FromWithAnEncodedOffset_BindsAsTheSameInstant()
    {
        // "+05:30" must travel as %2B05:30; a raw + would become a space and fail binding.
        var user = await SeedUserAsync();
        var resourceId = await SeedResourceAsync(capacity: 5);
        var start = FutureSlot();
        var id = await SeedBookingAsync(resourceId, user.Id, start);
        var admin = await CreateUserWithTokenAsync(UserRole.Admin);
        using var client = CreateClient(admin.Token);
        string Local(DateTime utc) =>
            Uri.EscapeDataString(new DateTimeOffset(utc).ToOffset(TimeSpan.FromHours(5.5)).ToString("yyyy-MM-ddTHH:mm:sszzz"));

        var atStart = await ReadPageAsync(
            await client.GetAsync($"{Routes.Booking}?resourceId={resourceId}&from={Local(start)}"));
        var afterStart = await ReadPageAsync(
            await client.GetAsync($"{Routes.Booking}?resourceId={resourceId}&from={Local(start.AddSeconds(1))}"));

        Assert.Equal([id], atStart.Data.Select(x => x.Id));
        Assert.Empty(afterStart.Data);
    }

    [Theory]
    [InlineData("pageSize=101")]
    [InlineData("page=0")]
    [InlineData("status=5")]
    [InlineData("status=Pending")]
    [InlineData("from=2026-10-02T00:00:00Z&to=2026-10-01T00:00:00Z")]
    public async Task Admin_InvalidQuery_Returns400ProblemDetails(string query)
    {
        var admin = await CreateUserWithTokenAsync(UserRole.Admin);
        using var client = CreateClient(admin.Token);

        var response = await client.GetAsync($"{Routes.Booking}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", MediaType(response));
    }
}
