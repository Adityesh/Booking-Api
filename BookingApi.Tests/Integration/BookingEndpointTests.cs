using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BookingApi.Data;
using BookingApi.Dto.Booking;
using BookingApi.Dto.Waitlist;
using BookingApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Tests.Integration;

// What POST/GET/cancel on /api/booking actually put on the wire: status, Content-Type, body and Location.
// The OpenAPI document (OpenApiContractTests) only describes responses; these tests prove the description is true,
// and the frontend's error wrapper depends on the content types (json vs problem+json vs text/plain).
[Collection("Postgres")]
public class BookingEndpointTests(PostgresFixture fixture) : ApiTestBase(fixture)
{
    // Case-insensitive because responses are camelCase. The enum converter reads both names and numbers, so these
    // tests keep working if the API switches enums to strings.
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static object CreateBody(int resourceId, DateTime start) =>
        new { startTime = start, endTime = start.AddHours(1), resourceId };

    private static string? MediaType(HttpResponseMessage response) => response.Content.Headers.ContentType?.MediaType;

    // ---------- 201 ----------

    [Fact]
    public async Task Create_WithCapacity_Returns201Json_WithLocationOfTheNewBooking()
    {
        var user = await CreateUserWithTokenAsync();
        var resourceId = await SeedResourceAsync(capacity: 1);
        using var client = CreateClient(user.Token);

        var response = await client.PostAsJsonAsync(Routes.Booking, CreateBody(resourceId, FutureSlot()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json", MediaType(response));
        var booking = await response.Content.ReadFromJsonAsync<BookingResponseDto>(Json);
        Assert.NotNull(booking);
        Assert.Equal(resourceId, booking.ResourceId);
        Assert.Equal(user.Id, booking.UserId);
        Assert.EndsWith($"/api/Booking/{booking.Id}", response.Headers.Location!.AbsolutePath,
            StringComparison.OrdinalIgnoreCase);
    }

    // ---------- 202 ----------

    [Fact]
    public async Task Create_WhenFull_Returns202Json_WithTheWaitlistEntry_AndALocationThatListsIt()
    {
        var holder = await SeedUserAsync();
        var user = await CreateUserWithTokenAsync();
        var resourceId = await SeedResourceAsync(capacity: 1);
        var slot = FutureSlot();
        await SeedBookingAsync(resourceId, holder.Id, slot);
        using var client = CreateClient(user.Token);

        var response = await client.PostAsJsonAsync(Routes.Booking, CreateBody(resourceId, slot));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("application/json", MediaType(response));

        // The body is the entry the database holds, not a message wrapper.
        var entry = await response.Content.ReadFromJsonAsync<WaitlistEntryResponseDto>(Json);
        Assert.NotNull(entry);
        await using (var context = CreateContext())
        {
            var row = await context.WaitlistEntries.AsNoTracking()
                .SingleAsync(w => w.ResourceId == resourceId && w.UserId == user.Id);
            Assert.Equal(row.Id, entry.Id);
        }
        Assert.Equal(resourceId, entry.ResourceId);
        Assert.Equal(slot, entry.RequestedStartTime);
        Assert.Equal(slot.AddHours(1), entry.RequestedEndTime);
        Assert.Equal(WaitlistStatus.Waiting, entry.Status);

        // Location points at the caller's waitlist, and following it really returns this entry.
        var location = response.Headers.Location;
        Assert.NotNull(location);
        Assert.Equal($"{Routes.Waitlist}/me", location.AbsolutePath, StringComparer.OrdinalIgnoreCase);

        var mine = await client.GetFromJsonAsync<List<WaitlistEntryResponseDto>>(location, Json);
        Assert.Contains(mine!, e => e.Id == entry.Id);
    }

    // ---------- error content types ----------

    [Fact]
    public async Task Create_AlreadyWaitListed_Returns409AsPlainText()
    {
        var holder = await SeedUserAsync();
        var user = await CreateUserWithTokenAsync();
        var resourceId = await SeedResourceAsync(capacity: 1);
        var slot = FutureSlot();
        await SeedBookingAsync(resourceId, holder.Id, slot);
        await SeedWaitlistEntryAsync(resourceId, user.Id, slot);
        using var client = CreateClient(user.Token);

        var response = await client.PostAsJsonAsync(Routes.Booking, CreateBody(resourceId, slot));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("text/plain", MediaType(response));
        Assert.Equal("User is already in the waitlist.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Create_UnknownResource_Returns404AsPlainText()
    {
        var user = await CreateUserWithTokenAsync();
        using var client = CreateClient(user.Token);

        var response = await client.PostAsJsonAsync(Routes.Booking, CreateBody(int.MaxValue, FutureSlot()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/plain", MediaType(response));
        Assert.Equal("Resource not found.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Create_InvalidTimes_Returns400AsProblemJson_WithFieldErrors()
    {
        var user = await CreateUserWithTokenAsync();
        var resourceId = await SeedResourceAsync();
        using var client = CreateClient(user.Token);
        var start = FutureSlot();

        var response = await client.PostAsJsonAsync(Routes.Booking,
            new { startTime = start, endTime = start.AddHours(-1), resourceId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", MediaType(response));
        using var doc = await ReadJsonAsync(response);
        Assert.Equal(400, doc.RootElement.GetProperty("status").GetInt32());
        Assert.NotEmpty(doc.RootElement.GetProperty("errors").EnumerateObject());
    }

    [Fact]
    public async Task GetById_Returns200Json_ForTheOwner_And404AsPlainText_ForAnyoneElse()
    {
        var owner = await CreateUserWithTokenAsync();
        var other = await CreateUserWithTokenAsync();
        var resourceId = await SeedResourceAsync();
        var bookingId = await SeedBookingAsync(resourceId, owner.Id);

        // Control: the owner can read it, so the 404 below can only mean "not yours".
        using var ownerClient = CreateClient(owner.Token);
        var ok = await ownerClient.GetAsync($"{Routes.Booking}/{bookingId}");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("application/json", MediaType(ok));

        using var otherClient = CreateClient(other.Token);
        var notFound = await otherClient.GetAsync($"{Routes.Booking}/{bookingId}");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal("text/plain", MediaType(notFound));
    }

    [Fact]
    public async Task Cancel_Returns204WithNoBody()
    {
        var owner = await CreateUserWithTokenAsync();
        var resourceId = await SeedResourceAsync();
        var bookingId = await SeedBookingAsync(resourceId, owner.Id);
        using var client = CreateClient(owner.Token);

        var response = await client.PostAsync($"{Routes.Booking}/{bookingId}/cancel", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }
}
