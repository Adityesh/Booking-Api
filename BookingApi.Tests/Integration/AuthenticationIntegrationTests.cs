using System.Net;
using System.Net.Http.Json;
using BookingApi.Data;
using BookingApi.Dto;
using BookingApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Tests.Integration;

[Collection("Postgres")]
public class AuthenticationIntegrationTests(PostgresFixture fixture) : ApiTestBase(fixture)
{
    private static string NewRegisterUsername() => $"reg_{Guid.NewGuid().ToString("N")[..12]}";

    // ======================= register =======================

    [Fact]
    public async Task Register_ValidRequest_ReturnsATokenThatOpensProtectedRoutes()
    {
        using var client = CreateClient();
        var username = NewRegisterUsername();

        var response = await client.PostAsJsonAsync(Routes.Register, new { username, password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.False(string.IsNullOrWhiteSpace(auth!.Token));
        Assert.Equal(username, auth.Username);

        using var authed = CreateClient(auth.Token);
        var mine = await authed.GetAsync($"{Routes.Waitlist}/me");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.Equal("[]", (await mine.Content.ReadAsStringAsync()).Trim());   // an empty list is 200 [], never 404
    }

    [Fact]
    public async Task Register_UsernameAlreadyTaken_Returns409()
    {
        using var client = CreateClient();
        var username = NewRegisterUsername();

        var first = await client.PostAsJsonAsync(Routes.Register, new { username, password = Password });
        var second = await client.PostAsJsonAsync(Routes.Register, new { username, password = Password });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Theory]
    [InlineData("ab", "Test_pass1")]            // username too short
    [InlineData("has space", "Test_pass1")]     // username with a character outside the whitelist
    [InlineData("valid_name", "short_1")]       // password under 8 characters
    [InlineData("valid_name", "nospecial123")]  // password without - _ or .
    public async Task Register_InvalidInput_Returns400_AndCreatesNoUser(string username, string password)
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(Routes.Register, new { username, password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.Users.AnyAsync(u => u.Username == username));
    }

    [Fact]
    public async Task Register_WithARoleInTheBody_StillCreatesARegularUser()
    {
        // The privilege-escalation fix, proven through the real pipeline: the role is never read from the client.
        using var client = CreateClient();
        var username = NewRegisterUsername();

        var response = await client.PostAsJsonAsync(Routes.Register, new { username, password = Password, role = "Admin" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>();

        await using (var context = CreateContext())
        {
            var user = await context.Users.AsNoTracking().SingleAsync(u => u.Username == username);
            Assert.Equal(UserRole.User, user.Role);
        }

        using var authed = CreateClient(auth!.Token);
        var adminRoute = await authed.GetAsync(Routes.AuditLog);
        Assert.Equal(HttpStatusCode.Forbidden, adminRoute.StatusCode);
    }

    // ======================= login =======================

    [Fact]
    public async Task Login_ValidCredentials_ReturnsAToken()
    {
        var user = await SeedUserAsync();
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(Routes.Login, new { username = user.Username, password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.False(string.IsNullOrWhiteSpace(auth!.Token));
        Assert.Equal(user.Username, auth.Username);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var user = await SeedUserAsync();
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(Routes.Login, new { username = user.Username, password = "Wrong_pass1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownUser_Returns401()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(Routes.Login, new { username = NewUsername(), password = Password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_InactiveUser_Returns401_EvenWithTheRightPassword()
    {
        var user = await SeedUserAsync(isActive: false);
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(Routes.Login, new { username = user.Username, password = Password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_EmptyFields_Returns400()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(Routes.Login, new { username = "", password = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ======================= token validation =======================

    [Fact]
    public async Task ProtectedRoute_WithATokenCraftedByTheTest_IsAccepted()
    {
        // The control for the next two tests: if this fails, the helper (or the JWT settings) is wrong,
        // and the "rejected" tests below would be passing for the wrong reason.
        var user = await SeedUserAsync();
        using var client = CreateClient(CreateToken(user.Id, UserRole.User));

        var response = await client.GetAsync($"{Routes.Waitlist}/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedRoute_GarbageToken_Returns401()
    {
        using var client = CreateClient("this-is-not-a-jwt");

        var response = await client.GetAsync($"{Routes.Waitlist}/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedRoute_TokenSignedWithADifferentKey_Returns401()
    {
        var user = await SeedUserAsync();
        var forged = CreateToken(user.Id, UserRole.Admin,
            signingKey: "a-completely-different-signing-key-also-longer-than-32-bytes");
        using var client = CreateClient(forged);

        var response = await client.GetAsync(Routes.AuditLog);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedRoute_ExpiredToken_Returns401()
    {
        var user = await SeedUserAsync();
        var expired = CreateToken(user.Id, UserRole.User, expires: DateTime.UtcNow.AddHours(-1));
        using var client = CreateClient(expired);

        var response = await client.GetAsync($"{Routes.Waitlist}/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}