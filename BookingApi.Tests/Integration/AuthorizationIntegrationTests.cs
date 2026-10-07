using System.Net;
using System.Net.Http.Json;
using BookingApi.Data;
using BookingApi.Jobs;
using BookingApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BookingApi.Tests.Integration;

[Collection("Postgres")]
public class AuthorizationIntegrationTests(PostgresFixture fixture) : ApiTestBase(fixture)
{
    // Every protected route in the API. Ids are placeholders: authorization runs before the action is reached.
    public static TheoryData<string, string> ProtectedRoutes => new()
    {
        { "GET", Routes.AuditLog },
        { "POST", Routes.Resource },
        { "PUT", $"{Routes.Resource}/1" },
        { "DELETE", $"{Routes.Resource}/1" },
        { "POST", Routes.Booking },
        { "GET", $"{Routes.Booking}/1" },
        { "POST", $"{Routes.Booking}/1/cancel" },
        { "GET", $"{Routes.Waitlist}/me" },
        { "POST", $"{Routes.Waitlist}/1/withdraw" }
    };

    // The routes only an Admin may call.
    public static TheoryData<string, string> AdminOnlyRoutes => new()
    {
        { "GET", Routes.AuditLog },
        { "POST", Routes.Resource },
        { "PUT", $"{Routes.Resource}/1" },
        { "DELETE", $"{Routes.Resource}/1" }
    };

    private static HttpRequestMessage Request(string method, string url) =>
        new(new HttpMethod(method), url)
        {
            Content = method is "POST" or "PUT" ? JsonContent.Create(new { }) : null
        };

    // ======================= who may call what =======================

    [Theory]
    [MemberData(nameof(ProtectedRoutes))]
    public async Task Anonymous_IsRejectedWith401_OnEveryProtectedRoute(string method, string url)
    {
        using var client = CreateClient();

        var response = await client.SendAsync(Request(method, url));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminOnlyRoutes))]
    public async Task NormalUser_IsRejectedWith403_OnAdminOnlyRoutes(string method, string url)
    {
        var user = await CreateUserWithTokenAsync();
        using var client = CreateClient(user.Token);

        var response = await client.SendAsync(Request(method, url));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanReadTheAuditLog()
    {
        var admin = await CreateUserWithTokenAsync(UserRole.Admin);
        using var client = CreateClient(admin.Token);

        var response = await client.GetAsync(Routes.AuditLog);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanCreateUpdateAndSoftDeleteAResource()
    {
        var admin = await CreateUserWithTokenAsync(UserRole.Admin);
        using var client = CreateClient(admin.Token);
        var type = (int)ResourceType.Room;   // numeric, so it works whether or not the API uses string enums

        var created = await client.PostAsJsonAsync(Routes.Resource, new { name = "Integration room", type, capacity = 3 });
        Assert.True(created.IsSuccessStatusCode, $"create returned {(int)created.StatusCode}");
        var id = await ReadIdAsync(created);

        var updated = await client.PutAsJsonAsync($"{Routes.Resource}/{id}", new { name = "Renamed room", type, capacity = 5 });
        Assert.True(updated.IsSuccessStatusCode, $"update returned {(int)updated.StatusCode}");

        var deleted = await client.DeleteAsync($"{Routes.Resource}/{id}");
        Assert.True(deleted.IsSuccessStatusCode, $"delete returned {(int)deleted.StatusCode}");

        await using var context = CreateContext();
        var resource = await context.Resources.AsNoTracking().SingleAsync(r => r.Id == id);
        Assert.Equal("Renamed room", resource.Name);
        Assert.Equal(5, resource.Capacity);
        Assert.False(resource.IsActive);   // soft delete: still there, just inactive
    }

    // ======================= ownership: "not yours" looks exactly like "doesn't exist" =======================

    [Fact]
    public async Task Booking_Get_IsVisibleToTheOwnerAndAnAdmin_ButNotFoundForAnotherUser()
    {
        var owner = await CreateUserWithTokenAsync();
        var intruder = await CreateUserWithTokenAsync();
        var admin = await CreateUserWithTokenAsync(UserRole.Admin);
        var bookingId = await SeedBookingAsync(await SeedResourceAsync(), owner.Id);
        var url = $"{Routes.Booking}/{bookingId}";

        using var ownerClient = CreateClient(owner.Token);
        using var intruderClient = CreateClient(intruder.Token);
        using var adminClient = CreateClient(admin.Token);

        // The owner's 200 is the control: it proves the route is right, so the 404 below means "not yours".
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruderClient.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync(url)).StatusCode);   // admin oversight
    }

    [Fact]
    public async Task Booking_Cancel_AnotherUserGets404_AndTheBookingIsUntouched_ThenTheOwnerCanCancel()
    {
        var owner = await CreateUserWithTokenAsync();
        var intruder = await CreateUserWithTokenAsync();
        var bookingId = await SeedBookingAsync(await SeedResourceAsync(), owner.Id);
        var url = $"{Routes.Booking}/{bookingId}/cancel";

        using var intruderClient = CreateClient(intruder.Token);
        var denied = await intruderClient.PostAsync(url, content: null);

        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(BookingStatus.Confirmed, await BookingStatusAsync(bookingId));

        // Control: the same route works for the owner, so the 404 above was about ownership, not a wrong URL.
        using var ownerClient = CreateClient(owner.Token);
        var allowed = await ownerClient.PostAsync(url, content: null);

        Assert.True(allowed.IsSuccessStatusCode, $"owner cancel returned {(int)allowed.StatusCode}");
        Assert.Equal(BookingStatus.Cancelled, await BookingStatusAsync(bookingId));
    }

    [Fact]
    public async Task Waitlist_Withdraw_AnotherUserGets404_AndTheEntryStaysWaiting_ThenTheOwnerCanWithdraw()
    {
        var owner = await CreateUserWithTokenAsync();
        var intruder = await CreateUserWithTokenAsync();
        var entryId = await SeedWaitlistEntryAsync(await SeedResourceAsync(), owner.Id);
        var url = $"{Routes.Waitlist}/{entryId}/withdraw";

        using var intruderClient = CreateClient(intruder.Token);
        var denied = await intruderClient.PostAsync(url, content: null);

        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(WaitlistStatus.Waiting, await WaitlistStatusAsync(entryId));

        using var ownerClient = CreateClient(owner.Token);
        var allowed = await ownerClient.PostAsync(url, content: null);

        Assert.True(allowed.IsSuccessStatusCode, $"owner withdraw returned {(int)allowed.StatusCode}");
        Assert.Equal(WaitlistStatus.Withdrawn, await WaitlistStatusAsync(entryId));
    }

    [Fact]
    public async Task Waitlist_Me_ReturnsOnlyTheCallersEntries()
    {
        var owner = await CreateUserWithTokenAsync();
        var other = await CreateUserWithTokenAsync();
        var entryId = await SeedWaitlistEntryAsync(await SeedResourceAsync(), owner.Id);

        using var ownerClient = CreateClient(owner.Token);
        using var otherClient = CreateClient(other.Token);

        var ownerResponse = await ownerClient.GetAsync($"{Routes.Waitlist}/me");
        var otherResponse = await otherClient.GetAsync($"{Routes.Waitlist}/me");

        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
        using (var doc = await ReadJsonAsync(ownerResponse))
        {
            Assert.Equal(1, doc.RootElement.GetArrayLength());
            Assert.Equal(entryId, doc.RootElement[0].GetProperty("id").GetInt32());
        }

        Assert.Equal(HttpStatusCode.OK, otherResponse.StatusCode);   // nothing of theirs: an empty list, not an error
        using (var doc = await ReadJsonAsync(otherResponse))
            Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    // ======================= the test host itself =======================

    [Fact]
    public void TestHost_DoesNotRunTheExpirationJob()
    {
        // Guards the factory: if the job were running, its startup sweep could expire rows other tests rely on.
        var hostedServices = Factory.Services.GetServices<IHostedService>();

        Assert.DoesNotContain(hostedServices, service => service is WaitlistExpirationService);
    }

    // ---------- small database reads ----------

    private async Task<BookingStatus> BookingStatusAsync(int bookingId)
    {
        await using var context = CreateContext();
        return await context.Bookings.AsNoTracking().Where(b => b.Id == bookingId).Select(b => b.Status).SingleAsync();
    }

    private async Task<WaitlistStatus> WaitlistStatusAsync(int entryId)
    {
        await using var context = CreateContext();
        return await context.WaitlistEntries.AsNoTracking().Where(w => w.Id == entryId).Select(w => w.Status).SingleAsync();
    }
}