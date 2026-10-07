using System.Net;
using System.Net.Http.Json;
using BookingApi.Data;
using BookingApi.Tests.Fixtures;

namespace BookingApi.Tests.Integration;

[Collection("Postgres")]
public class AuditLogEndpointTests(PostgresFixture fixture) : ApiTestBase(fixture)
{
    // Seeded audit entries live in a day in the year 1300, far from the real-present entries every other test
    // writes and from the 1501+ windows used by AuditLogQueryTests, so counts here are exact.
    private static readonly DateTime SeededDay = new(1300, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const string SeededWindow = "from=1300-01-01T00:00:00Z&to=1300-01-02T00:00:00Z";

    [Fact]
    public async Task NoQueryParameters_Returns200_WithTheDefaultPaging()
    {
        // Proves the Page = 1 / PageSize = 20 defaults on the record really apply when the query string omits them.
        var admin = await CreateUserWithTokenAsync(UserRole.Admin);
        using var client = CreateClient(admin.Token);

        var response = await client.GetAsync(Routes.AuditLog);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await ReadJsonAsync(response);
        Assert.Equal(1, doc.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(20, doc.RootElement.GetProperty("pageSize").GetInt32());
    }

    [Theory]
    [InlineData("pageSize=101")]
    [InlineData("pageSize=0")]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("action=NotARealAction")]
    [InlineData("systemOnly=true&userId=1")]
    [InlineData("from=2026-10-02T00:00:00Z&to=2026-10-01T00:00:00Z")]
    public async Task InvalidQuery_Returns400(string query)
    {
        var admin = await CreateUserWithTokenAsync(UserRole.Admin);
        using var client = CreateClient(admin.Token);

        var response = await client.GetAsync($"{Routes.AuditLog}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SystemOnlyAndDateRange_BindFromTheQueryString_AndFilterCorrectly()
    {
        // The unit tests call the service directly; this goes through real model binding for the bool and the dates.
        var user = await SeedUserAsync();
        await SeedAuditEntryAsync(SeededDay.AddHours(1), ActionType.WaitlistEntryExpired, userId: null);
        await SeedAuditEntryAsync(SeededDay.AddHours(2), ActionType.BookingCreated, userId: user.Id);
        var admin = await CreateUserWithTokenAsync(UserRole.Admin);
        using var client = CreateClient(admin.Token);

        var everything = await client.GetAsync($"{Routes.AuditLog}?{SeededWindow}");
        var systemOnly = await client.GetAsync($"{Routes.AuditLog}?{SeededWindow}&systemOnly=true");

        using (var doc = await ReadJsonAsync(everything))
            Assert.Equal(2, doc.RootElement.GetProperty("count").GetInt32());

        using (var doc = await ReadJsonAsync(systemOnly))
        {
            Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
            Assert.Equal(JsonValueKindNull, doc.RootElement.GetProperty("data")[0].GetProperty("userId").ValueKind);
        }
    }

    [Fact]
    public async Task CreatingAndCancellingABooking_LeavesBothEntriesInTheLog_AttributedToThatUser()
    {
        // The whole chain through the real pipeline: request -> service -> audit entry in the same transaction -> admin read.
        var user = await CreateUserWithTokenAsync();
        var admin = await CreateUserWithTokenAsync(UserRole.Admin);
        var resourceId = await SeedResourceAsync(capacity: 1);
        var start = FutureSlot();

        using var userClient = CreateClient(user.Token);
        var created = await userClient.PostAsJsonAsync(Routes.Booking, new { startTime = start, endTime = start.AddHours(1), resourceId });
        Assert.True(created.IsSuccessStatusCode, $"create returned {(int)created.StatusCode}");
        var bookingId = await ReadIdAsync(created);

        var cancelled = await userClient.PostAsync($"{Routes.Booking}/{bookingId}/cancel", content: null);
        Assert.True(cancelled.IsSuccessStatusCode, $"cancel returned {(int)cancelled.StatusCode}");

        using var adminClient = CreateClient(admin.Token);

        var all = await adminClient.GetAsync($"{Routes.AuditLog}?userId={user.Id}");
        using (var doc = await ReadJsonAsync(all))
        {
            Assert.Equal(2, doc.RootElement.GetProperty("count").GetInt32());
            foreach (var entry in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                Assert.Equal(bookingId, entry.GetProperty("entityId").GetInt32());
                Assert.Equal(user.Id, entry.GetProperty("userId").GetInt32());
            }
        }

        var createdOnly = await adminClient.GetAsync($"{Routes.AuditLog}?userId={user.Id}&action=BookingCreated");
        using (var doc = await ReadJsonAsync(createdOnly))
            Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
    }

    // ---------- helpers ----------

    private const System.Text.Json.JsonValueKind JsonValueKindNull = System.Text.Json.JsonValueKind.Null;

    private async Task SeedAuditEntryAsync(DateTime timestamp, ActionType action, int? userId)
    {
        await using var context = CreateContext();
        context.AuditLogEntries.Add(new AuditLogEntryEntity
        {
            Timestamp = timestamp,
            Action = action,
            EntityId = 1,
            UserId = userId
        });
        await context.SaveChangesAsync();
    }
}