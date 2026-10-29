using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using BookingApi.Data;
using BookingApi.Dto;
using BookingApi.Tests.Fixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace BookingApi.Tests.Integration;

// The refresh-token cookie over the real pipeline: login/register set it, refresh rotates it, logout clears it.
// Most tests send the cookie by hand (a client that does NOT keep cookies), so every request says exactly which token it
// carries. One test uses a cookie-keeping client instead, to prove a browser would actually send the cookie back.
[Collection("Postgres")]
public class RefreshTokenEndpointTests(PostgresFixture fixture) : ApiTestBase(fixture)
{
    private const string CookieName = "refresh-token";

    private HttpClient CreateCookielessClient(string? accessToken = null)
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        if (accessToken is not null)
            client.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);
        return client;
    }

    private static string Hash(string raw) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    // The refresh-token Set-Cookie of a response; fails the test if there isn't exactly one.
    private static SetCookieHeaderValue RefreshCookie(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues(HeaderNames.SetCookie, out var values), "No Set-Cookie header");
        var cookies = SetCookieHeaderValue.ParseList(values!.ToList());
        return Assert.Single(cookies, c => c.Name.Value == CookieName);
    }

    private static void AssertDeletesTheCookie(HttpResponseMessage response)
    {
        var cookie = RefreshCookie(response);
        Assert.Equal("", cookie.Value.Value);
        Assert.True(cookie.Expires < DateTimeOffset.UtcNow, "The cookie should be expired");
        // Same path as the real cookie, otherwise the browser deletes a different cookie and keeps the live one.
        Assert.Equal("/api/Auth", cookie.Path.Value);
    }

    private static HttpRequestMessage WithCookie(string route, string? refreshToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route);
        if (refreshToken is not null) request.Headers.Add(HeaderNames.Cookie, $"{CookieName}={refreshToken}");
        return request;
    }

    private async Task<HttpResponseMessage> RefreshAsync(string? refreshToken)
    {
        using var client = CreateCookielessClient();
        return await client.SendAsync(WithCookie(Routes.Refresh, refreshToken));
    }

    private async Task<HttpResponseMessage> LogoutAsync(string? refreshToken)
    {
        using var client = CreateCookielessClient();
        return await client.SendAsync(WithCookie(Routes.Logout, refreshToken));
    }

    // Logs a seeded user in and returns the refresh-token cookie value.
    private async Task<string> LoginForCookieAsync(string username)
    {
        using var client = CreateCookielessClient();
        var response = await client.PostAsJsonAsync(Routes.Login, new { username, password = Password });
        response.EnsureSuccessStatusCode();
        return RefreshCookie(response).Value.Value!;
    }

    private async Task<RefreshTokenEntity> GetRowAsync(string raw)
    {
        await using var context = CreateContext();
        var hash = Hash(raw);
        return await context.RefreshTokens.AsNoTracking().SingleAsync(r => r.TokenHash == hash);
    }

    // ======================= login / register =======================

    [Fact]
    public async Task Login_SetsAnHttpOnlySecureStrictCookieScopedToTheAuthRoutes()
    {
        var user = await SeedUserAsync();
        using var client = CreateCookielessClient();
        var before = DateTimeOffset.UtcNow;

        var response = await client.PostAsJsonAsync(Routes.Login, new { username = user.Username, password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = RefreshCookie(response);
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/api/Auth", cookie.Path.Value);
        Assert.True(cookie.Domain.Value is null, "Host-only: no Domain attribute");
        // Cookie expiry has one-second resolution.
        Assert.InRange(cookie.Expires!.Value, before.AddDays(14).AddSeconds(-2), DateTimeOffset.UtcNow.AddDays(14).AddSeconds(2));
        Assert.True(response.Headers.CacheControl?.NoStore, "Token responses must not be cached");

        // The value really is a stored token for this user (as a hash, not the raw value).
        var row = await GetRowAsync(cookie.Value.Value!);
        Assert.Equal(user.Id, row.UserId);
    }

    [Fact]
    public async Task Login_TheRefreshTokenNeverAppearsInTheJsonBody()
    {
        var user = await SeedUserAsync();
        using var client = CreateCookielessClient();

        var response = await client.PostAsJsonAsync(Routes.Login, new { username = user.Username, password = Password });

        var raw = RefreshCookie(response).Value.Value!;
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(raw, body);
        // Exactly the AuthResponseDto shape, so the frontend's generated types still hold.
        using var doc = await ReadJsonAsync(response);
        Assert.Equal(
            ["expiresAt", "role", "token", "username"],
            doc.RootElement.EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task Login_BadCredentials_SetsNoCookie()
    {
        var user = await SeedUserAsync();
        using var client = CreateCookielessClient();

        var response = await client.PostAsJsonAsync(Routes.Login, new { username = user.Username, password = "Wrong_pass1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains(HeaderNames.SetCookie));
    }

    [Fact]
    public async Task Register_SetsTheSameCookie()
    {
        using var client = CreateCookielessClient();
        var username = NewUsername();

        var response = await client.PostAsJsonAsync(Routes.Register, new { username, email = Email, password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = RefreshCookie(response);
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal("/api/Auth", cookie.Path.Value);
        Assert.True(response.Headers.CacheControl?.NoStore);
        // A token for the newly created user.
        await using var context = CreateContext();
        var userId = await context.Users.Where(u => u.Username == username).Select(u => u.Id).SingleAsync();
        Assert.Equal(userId, (await GetRowAsync(cookie.Value.Value!)).UserId);
    }

    // ======================= refresh =======================

    [Fact]
    public async Task Refresh_ValidCookie_Returns200WithAWorkingAccessToken_AndRotatesTheCookie()
    {
        var user = await SeedUserAsync();
        var original = await LoginForCookieAsync(user.Username);

        var response = await RefreshAsync(original);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>(ApiJson);
        Assert.Equal(user.Username, auth!.Username);
        Assert.Equal(UserRole.User, auth.Role);

        var rotated = RefreshCookie(response);
        Assert.NotEqual(original, rotated.Value.Value);
        Assert.True(rotated.HttpOnly);
        Assert.Equal("/api/Auth", rotated.Path.Value);

        // The new access token is a real one.
        using var authed = CreateClient(auth.Token);
        Assert.Equal(HttpStatusCode.OK, (await authed.GetAsync($"{Routes.Waitlist}/me")).StatusCode);
    }

    [Fact]
    public async Task Refresh_TheOldCookieReplayedRightAway_Returns401_ButTheNewOneKeepsWorking()
    {
        var user = await SeedUserAsync();
        var original = await LoginForCookieAsync(user.Username);
        var rotated = RefreshCookie(await RefreshAsync(original)).Value.Value!;

        var replay = await RefreshAsync(original);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        // Inside the grace window: a late tab, not a thief. Nothing else is revoked.
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(rotated)).StatusCode);
    }

    [Fact]
    public async Task Refresh_OldCookieReplayedAfterTheGraceWindow_Returns401_AndKillsTheNewerCookieToo()
    {
        var user = await SeedUserAsync();
        var original = await LoginForCookieAsync(user.Username);
        var rotated = RefreshCookie(await RefreshAsync(original)).Value.Value!;
        await using (var context = CreateContext())
        {
            var hash = Hash(original);
            await context.RefreshTokens.Where(r => r.TokenHash == hash)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAt, DateTime.UtcNow.AddMinutes(-5)));
        }

        var replay = await RefreshAsync(original);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        // Same 401 and same message as any other failure: the caller can't tell reuse was detected.
        Assert.Equal("Session expired", await replay.Content.ReadAsStringAsync());
        AssertDeletesTheCookie(replay);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(rotated)).StatusCode);
        Assert.Equal(RefreshTokenRevokedReason.ReuseDetected, (await GetRowAsync(rotated)).RevokedReason);
    }

    [Fact]
    public async Task Refresh_NoCookie_Returns401()
    {
        var response = await RefreshAsync(null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_UnknownCookie_Returns401_AndDeletesTheCookie()
    {
        var response = await RefreshAsync("not-a-token-anyone-was-issued");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertDeletesTheCookie(response);
    }

    [Fact]
    public async Task Refresh_UserDeactivatedAfterLogin_Returns401()
    {
        var user = await SeedUserAsync();
        var cookie = await LoginForCookieAsync(user.Username);
        await using (var context = CreateContext())
        {
            await context.Users.Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        }

        var response = await RefreshAsync(cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_ABrowserLikeClient_SendsTheCookieBackOnItsOwn()
    {
        // A cookie-keeping client applies Path and Secure the way a browser does. If the cookie's Path didn't match
        // the real route's casing, it would never be sent and this refresh would be a 401.
        var user = await SeedUserAsync();
        using var browser = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        (await browser.PostAsJsonAsync(Routes.Login, new { username = user.Username, password = Password }))
            .EnsureSuccessStatusCode();

        var first = await browser.PostAsync(Routes.Refresh, null);
        var second = await browser.PostAsync(Routes.Refresh, null);   // must carry the ROTATED cookie, not the original

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    // ======================= logout =======================

    [Fact]
    public async Task Logout_Returns204_DeletesTheCookie_AndTheTokenNoLongerRefreshes()
    {
        var user = await SeedUserAsync();
        var cookie = await LoginForCookieAsync(user.Username);

        var response = await LogoutAsync(cookie);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertDeletesTheCookie(response);
        Assert.Equal(RefreshTokenRevokedReason.Logout, (await GetRowAsync(cookie)).RevokedReason);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(cookie)).StatusCode);
    }

    [Fact]
    public async Task Logout_OnlyEndsThisDevicesSession()
    {
        var user = await SeedUserAsync();
        var laptop = await LoginForCookieAsync(user.Username);
        var phone = await LoginForCookieAsync(user.Username);

        await LogoutAsync(laptop);

        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(phone)).StatusCode);
    }

    [Fact]
    public async Task Logout_IsIdempotent_AndWorksWithoutACookie()
    {
        var user = await SeedUserAsync();
        var cookie = await LoginForCookieAsync(user.Username);
        await LogoutAsync(cookie);

        var again = await LogoutAsync(cookie);
        var withoutCookie = await LogoutAsync(null);

        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, withoutCookie.StatusCode);
        AssertDeletesTheCookie(withoutCookie);
    }
}
