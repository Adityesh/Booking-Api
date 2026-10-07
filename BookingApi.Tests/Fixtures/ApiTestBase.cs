using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BookingApi.Data;
using BookingApi.Dto;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace BookingApi.Tests.Fixtures;

// The one place to change if a route differs from what these tests assume.
public static class Routes
{
    public const string Register = "/api/auth/register";
    public const string Login = "/api/auth/login";
    public const string Resource = "/api/resource";
    public const string Booking = "/api/booking";
    public const string Waitlist = "/api/waitlist";
    public const string AuditLog = "/api/auditlog";
}

public sealed record SeededUser(int Id, string Username);

public sealed record AuthedUser(int Id, string Username, string Token);

// Every test gets its own host (a fresh in-memory copy of the real application) over the shared test database.
// Users are inserted straight into the database with a known password and then log in through the real API,
// so the tokens used in these tests are real ones.
public abstract class ApiTestBase : IDisposable
{
    protected const string Password = "Test_pass1";

    private readonly string _connectionString;

    protected ApiFactory Factory { get; }

    protected ApiTestBase(PostgresFixture fixture)
    {
        _connectionString = fixture.ConnectionString;
        Factory = new ApiFactory(_connectionString);
    }

    public void Dispose() => Factory.Dispose();

    // ---------- clients ----------

    protected HttpClient CreateClient(string? token = null)
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),   // https, so UseHttpsRedirection never answers with a redirect
            AllowAutoRedirect = false
        });

        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    // ---------- database ----------

    protected AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options);
    }

    protected static string NewUsername() => $"u_{Guid.NewGuid().ToString("N")[..20]}";

    protected async Task<SeededUser> SeedUserAsync(UserRole role = UserRole.User, bool isActive = true)
    {
        await using var context = CreateContext();
        var user = new UserEntity
        {
            Username = NewUsername(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, 4),   // lowest work factor: fast tests, same verification
            Role = role,
            IsActive = isActive
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return new SeededUser(user.Id, user.Username);
    }

    protected async Task<int> SeedResourceAsync(int capacity = 1, bool isActive = true)
    {
        await using var context = CreateContext();
        var resource = new ResourceEntity
        {
            Name = $"resource_{Guid.NewGuid():N}",
            Type = ResourceType.Room,
            Capacity = capacity,
            IsActive = isActive
        };
        context.Resources.Add(resource);
        await context.SaveChangesAsync();
        return resource.Id;
    }

    // Three days out by default: comfortably outside the 2-hour cancellation window and still promotable.
    protected static DateTime FutureSlot(int daysAhead = 3, int hour = 10) =>
        DateTime.UtcNow.Date.AddDays(daysAhead).AddHours(hour);

    protected async Task<int> SeedBookingAsync(int resourceId, int userId, DateTime? start = null)
    {
        var from = start ?? FutureSlot();
        await using var context = CreateContext();
        var booking = new BookingEntity
        {
            ResourceId = resourceId,
            UserId = userId,
            StartTime = from,
            EndTime = from.AddHours(1),
            Status = BookingStatus.Confirmed
        };
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();
        return booking.Id;
    }

    protected async Task<int> SeedWaitlistEntryAsync(int resourceId, int userId, DateTime? start = null)
    {
        var from = start ?? FutureSlot();
        await using var context = CreateContext();
        var entry = new WaitlistEntryEntity
        {
            ResourceId = resourceId,
            UserId = userId,
            RequestedStartTime = from,
            RequestedEndTime = from.AddHours(1),
            Status = WaitlistStatus.Waiting,
            CreatedAt = DateTime.UtcNow
        };
        context.WaitlistEntries.Add(entry);
        await context.SaveChangesAsync();
        return entry.Id;
    }

    // ---------- authentication ----------

    protected async Task<string> LoginAsync(string username)
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync(Routes.Login, new { username, password = Password });
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        return auth!.Token;
    }

    protected async Task<AuthedUser> CreateUserWithTokenAsync(UserRole role = UserRole.User)
    {
        var user = await SeedUserAsync(role);
        return new AuthedUser(user.Id, user.Username, await LoginAsync(user.Username));
    }

    // A token minted by the test itself. With the defaults it is indistinguishable from a real one,
    // which is what makes the "wrong key" and "expired" variants meaningful.
    protected static string CreateToken(int userId, UserRole role, string? signingKey = null, DateTime? expires = null)
    {
        var expiry = expires ?? DateTime.UtcNow.AddHours(1);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? TestJwt.Key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TestJwt.Issuer,
            audience: TestJwt.Audience,
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, "crafted"),
                new Claim(ClaimTypes.Role, role.ToString())
            ],
            notBefore: expiry.AddHours(-2),
            expires: expiry,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // ---------- response helpers ----------

    protected static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    protected static async Task<int> ReadIdAsync(HttpResponseMessage response)
    {
        using var doc = await ReadJsonAsync(response);
        return doc.RootElement.GetProperty("id").GetInt32();
    }
}