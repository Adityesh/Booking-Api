using System.Security.Cryptography;
using System.Text;
using BookingApi.Data;
using BookingApi.Dto.RefreshToken;
using BookingApi.Helpers;
using BookingApi.Service;
using BookingApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookingApi.Tests.Service;

// RefreshTokenService against real Postgres: rotation relies on ExecuteUpdate + a transaction, which InMemory can't run.
// Every test seeds its own user, so its tokens (one family per IssueAsync) never meet another test's.
// The service reads the real clock, so "time passing" is simulated by moving the rows' timestamps into the past.
[Collection("Postgres")]
public class RefreshTokenServiceTests(PostgresFixture fixture)
{
    private const int LifetimeDays = 14;
    private const int AbsoluteLifetimeDays = 30;
    private const int GraceSeconds = 10;

    // Postgres stores microseconds, .NET ticks are 100 ns: values read back can differ by under a microsecond.
    private static readonly TimeSpan DbPrecision = TimeSpan.FromMilliseconds(1);

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options);
    }

    private static RefreshTokenService NewService(AppDbContext context) =>
        new(context, Options.Create(new RefreshTokenOptions
        {
            LifetimeDays = LifetimeDays,
            AbsoluteLifetimeDays = AbsoluteLifetimeDays,
            ReuseGraceSeconds = GraceSeconds
        }), NullLogger<RefreshTokenService>.Instance);

    // Same format as the service: lowercase SHA-256 hex. Recomputed here so the test proves what is stored.
    private static string Hash(string raw) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    // ---------- seeding / reading ----------

    private async Task<UserEntity> SeedUserAsync(bool isActive = true)
    {
        await using var context = CreateContext();
        var user = new UserEntity
        {
            Username = $"rt_{Guid.NewGuid():N}",
            PasswordHash = "not-a-real-hash",
            Role = UserRole.User,
            IsActive = isActive
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private async Task<string> IssueAsync(int userId)
    {
        await using var context = CreateContext();
        var (_, raw) = await NewService(context).IssueAsync(userId, CancellationToken.None);
        return raw;
    }

    private async Task<RotateRefreshTokenResponseDto> RotateAsync(string raw)
    {
        await using var context = CreateContext();
        return await NewService(context).RotateAsync(raw, CancellationToken.None);
    }

    private async Task<bool> RevokeAsync(string raw)
    {
        await using var context = CreateContext();
        return await NewService(context).RevokeAsync(raw, CancellationToken.None);
    }

    private async Task<RefreshTokenEntity> GetRowAsync(string raw)
    {
        await using var context = CreateContext();
        var hash = Hash(raw);
        return await context.RefreshTokens.AsNoTracking().SingleAsync(r => r.TokenHash == hash);
    }

    private async Task<List<RefreshTokenEntity>> GetUserRowsAsync(int userId)
    {
        await using var context = CreateContext();
        return await context.RefreshTokens.AsNoTracking().Where(r => r.UserId == userId).OrderBy(r => r.Id).ToListAsync();
    }

    // Rewrites a token's timestamps, to simulate a token issued or rotated a while ago.
    private async Task UpdateRowAsync(string raw, Action<RefreshTokenEntity> change)
    {
        await using var context = CreateContext();
        var hash = Hash(raw);
        var row = await context.RefreshTokens.SingleAsync(r => r.TokenHash == hash);
        change(row);
        await context.SaveChangesAsync();
    }

    // ---------- IssueAsync ----------

    [Fact]
    public async Task IssueAsync_StoresOnlyTheHash_WithBothExpiriesFromTheSameInstant()
    {
        var user = await SeedUserAsync();

        await using var context = CreateContext();
        var (expiresAt, raw) = await NewService(context).IssueAsync(user.Id, CancellationToken.None);

        var row = Assert.Single(await GetUserRowsAsync(user.Id));
        Assert.Equal(Hash(raw), row.TokenHash);
        Assert.NotEqual(raw, row.TokenHash);
        Assert.Equal(64, row.TokenHash.Length);
        Assert.NotEqual(Guid.Empty, row.FamilyId);
        Assert.Null(row.RevokedAt);
        Assert.Null(row.RevokedReason);
        Assert.Null(row.ReplacedById);
        // Exact differences: all three dates come from one `now`.
        Assert.Equal(TimeSpan.FromDays(LifetimeDays), row.ExpiresAt - row.CreatedAt);
        Assert.Equal(TimeSpan.FromDays(AbsoluteLifetimeDays), row.AbsoluteExpiresAt - row.CreatedAt);
        Assert.Equal(row.ExpiresAt, expiresAt, DbPrecision);
    }

    [Fact]
    public async Task IssueAsync_EveryLoginStartsItsOwnFamily()
    {
        var user = await SeedUserAsync();

        var first = await IssueAsync(user.Id);
        var second = await IssueAsync(user.Id);

        Assert.NotEqual(first, second);
        var rows = await GetUserRowsAsync(user.Id);
        Assert.Equal(2, rows.Count);
        Assert.NotEqual(rows[0].FamilyId, rows[1].FamilyId);
    }

    // ---------- RotateAsync: success ----------

    [Fact]
    public async Task RotateAsync_ActiveToken_IssuesANewTokenInTheSameFamily_AndLinksTheOldOneToIt()
    {
        var user = await SeedUserAsync();
        var raw = await IssueAsync(user.Id);
        var before = await GetRowAsync(raw);

        var result = await RotateAsync(raw);

        Assert.Equal(RotateRefreshTokenResult.Success, result.Result);
        Assert.NotNull(result.NewToken);
        Assert.NotEqual(raw, result.NewToken);
        Assert.Equal(new TokenUserDto(user.Id, user.Role, user.Username), result.TokenUser);

        var old = await GetRowAsync(raw);
        var next = await GetRowAsync(result.NewToken);
        Assert.Equal(RefreshTokenRevokedReason.Rotated, old.RevokedReason);
        Assert.NotNull(old.RevokedAt);
        Assert.Equal(next.Id, old.ReplacedById);

        Assert.Equal(before.FamilyId, next.FamilyId);
        Assert.Equal(user.Id, next.UserId);
        // Copied, not recomputed: this is what makes the 30-day cap absolute.
        Assert.Equal(before.AbsoluteExpiresAt, next.AbsoluteExpiresAt);
        Assert.Null(next.RevokedAt);
        Assert.Equal(next.ExpiresAt, result.ExpiresAt!.Value, DbPrecision);
    }

    [Fact]
    public async Task RotateAsync_SlidesTheExpiry_ToAFullLifetimeFromNow()
    {
        var user = await SeedUserAsync();
        var raw = await IssueAsync(user.Id);
        // As if issued 13 days ago: one day left.
        await UpdateRowAsync(raw, r => r.ExpiresAt = DateTime.UtcNow.AddDays(1));

        var start = DateTime.UtcNow;
        var result = await RotateAsync(raw);
        var end = DateTime.UtcNow;

        Assert.Equal(RotateRefreshTokenResult.Success, result.Result);
        var next = await GetRowAsync(result.NewToken!);
        Assert.InRange(next.ExpiresAt, start.AddDays(LifetimeDays) - DbPrecision, end.AddDays(LifetimeDays));
    }

    [Fact]
    public async Task RotateAsync_NeverExtendsPastTheAbsoluteCap()
    {
        var user = await SeedUserAsync();
        var raw = await IssueAsync(user.Id);
        // As if logged in 28 days ago: the cap is 2 days away, closer than a fresh 14-day lifetime.
        var cap = DateTime.UtcNow.AddDays(2);
        await UpdateRowAsync(raw, r => r.AbsoluteExpiresAt = cap);

        var result = await RotateAsync(raw);

        Assert.Equal(RotateRefreshTokenResult.Success, result.Result);
        var next = await GetRowAsync(result.NewToken!);
        Assert.Equal(cap, next.ExpiresAt, DbPrecision);
        Assert.Equal(cap, next.AbsoluteExpiresAt, DbPrecision);
        Assert.Equal(cap, result.ExpiresAt!.Value, DbPrecision);
    }

    [Fact]
    public async Task RotateAsync_TheNewTokenCanItselfBeRotated()
    {
        var user = await SeedUserAsync();
        var raw = await IssueAsync(user.Id);

        var second = await RotateAsync(raw);
        var third = await RotateAsync(second.NewToken!);

        Assert.Equal(RotateRefreshTokenResult.Success, third.Result);
        var rows = await GetUserRowsAsync(user.Id);
        Assert.Equal(3, rows.Count);
        Assert.Single(rows, r => r.RevokedAt == null);
        Assert.Single(rows.Select(r => r.FamilyId).Distinct());
    }

    // ---------- RotateAsync: rejected ----------

    [Fact]
    public async Task RotateAsync_UnknownToken_IsInvalid()
    {
        var result = await RotateAsync("not-a-token-anyone-was-issued");

        Assert.Equal(new RotateRefreshTokenResponseDto(RotateRefreshTokenResult.Invalid), result);
    }

    [Fact]
    public async Task RotateAsync_ExpiredToken_IsInvalid_AndIssuesNothing()
    {
        var user = await SeedUserAsync();
        var raw = await IssueAsync(user.Id);
        await UpdateRowAsync(raw, r => r.ExpiresAt = DateTime.UtcNow.AddMinutes(-1));

        var result = await RotateAsync(raw);

        Assert.Equal(new RotateRefreshTokenResponseDto(RotateRefreshTokenResult.Invalid), result);
        var row = Assert.Single(await GetUserRowsAsync(user.Id));
        Assert.Null(row.RevokedAt);
    }

    [Fact]
    public async Task RotateAsync_PastTheAbsoluteCap_IsInvalid_EvenWithSlidingTimeLeft()
    {
        var user = await SeedUserAsync();
        var raw = await IssueAsync(user.Id);
        await UpdateRowAsync(raw, r => r.AbsoluteExpiresAt = DateTime.UtcNow.AddMinutes(-1));

        var result = await RotateAsync(raw);

        Assert.Equal(new RotateRefreshTokenResponseDto(RotateRefreshTokenResult.Invalid), result);
        Assert.Single(await GetUserRowsAsync(user.Id));
    }

    [Fact]
    public async Task RotateAsync_InactiveUser_IsInvalid()
    {
        var user = await SeedUserAsync();
        var raw = await IssueAsync(user.Id);
        await using (var context = CreateContext())
        {
            await context.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        }

        var result = await RotateAsync(raw);

        Assert.Equal(new RotateRefreshTokenResponseDto(RotateRefreshTokenResult.Invalid), result);
        Assert.Single(await GetUserRowsAsync(user.Id));
    }

    [Fact]
    public async Task RotateAsync_LoggedOutToken_IsInvalid()
    {
        var user = await SeedUserAsync();
        var raw = await IssueAsync(user.Id);
        Assert.True(await RevokeAsync(raw));

        var result = await RotateAsync(raw);

        Assert.Equal(new RotateRefreshTokenResponseDto(RotateRefreshTokenResult.Invalid), result);
        var row = Assert.Single(await GetUserRowsAsync(user.Id));
        Assert.Equal(RefreshTokenRevokedReason.Logout, row.RevokedReason);
    }

    // ---------- RotateAsync: replaying a rotated token ----------

    [Fact]
    public async Task RotateAsync_RotatedTokenReplayedWithinTheGraceWindow_IsInvalid_AndRevokesNothing()
    {
        // A second tab, or a retry whose first response was lost: the client is legitimate, just late.
        var user = await SeedUserAsync();
        var raw = await IssueAsync(user.Id);
        var first = await RotateAsync(raw);

        var replay = await RotateAsync(raw);

        Assert.Equal(new RotateRefreshTokenResponseDto(RotateRefreshTokenResult.Invalid), replay);
        var next = await GetRowAsync(first.NewToken!);
        Assert.Null(next.RevokedAt);
        Assert.Equal(2, (await GetUserRowsAsync(user.Id)).Count);
        // The token the winner received still works.
        Assert.Equal(RotateRefreshTokenResult.Success, (await RotateAsync(first.NewToken!)).Result);
    }

    [Fact]
    public async Task RotateAsync_RotatedTokenReplayedAfterTheGraceWindow_RevokesTheWholeFamily()
    {
        var user = await SeedUserAsync();
        var raw = await IssueAsync(user.Id);
        var first = await RotateAsync(raw);
        await UpdateRowAsync(raw, r => r.RevokedAt = DateTime.UtcNow.AddSeconds(-(GraceSeconds + 5)));

        var replay = await RotateAsync(raw);

        Assert.Equal(new RotateRefreshTokenResponseDto(RotateRefreshTokenResult.ReuseDetected), replay);
        // The live descendant dies with it: we can't tell whether the user or the thief holds it.
        var next = await GetRowAsync(first.NewToken!);
        Assert.Equal(RefreshTokenRevokedReason.ReuseDetected, next.RevokedReason);
        Assert.NotNull(next.RevokedAt);
        // The replayed token keeps its original reason (the family UPDATE only touches rows that were still active).
        Assert.Equal(RefreshTokenRevokedReason.Rotated, (await GetRowAsync(raw)).RevokedReason);
        Assert.Equal(new RotateRefreshTokenResponseDto(RotateRefreshTokenResult.Invalid), await RotateAsync(first.NewToken!));
    }

    [Fact]
    public async Task RotateAsync_ReplayOfAnExpiredRotatedToken_IsStillDetectedAsReuse()
    {
        // Stolen on day 1, replayed on day 16: past its own expiry, but its descendant is live, so it must still trip.
        var user = await SeedUserAsync();
        var raw = await IssueAsync(user.Id);
        var first = await RotateAsync(raw);
        await UpdateRowAsync(raw, r =>
        {
            r.RevokedAt = DateTime.UtcNow.AddDays(-15);
            r.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        });

        var replay = await RotateAsync(raw);

        Assert.Equal(RotateRefreshTokenResult.ReuseDetected, replay.Result);
        Assert.Equal(RefreshTokenRevokedReason.ReuseDetected, (await GetRowAsync(first.NewToken!)).RevokedReason);
    }

    [Fact]
    public async Task RotateAsync_ReuseInOneFamily_LeavesTheUsersOtherSessionsAlone()
    {
        var user = await SeedUserAsync();
        var laptop = await IssueAsync(user.Id);
        var phone = await IssueAsync(user.Id);
        await RotateAsync(laptop);
        await UpdateRowAsync(laptop, r => r.RevokedAt = DateTime.UtcNow.AddHours(-1));

        Assert.Equal(RotateRefreshTokenResult.ReuseDetected, (await RotateAsync(laptop)).Result);

        Assert.Null((await GetRowAsync(phone)).RevokedAt);
        Assert.Equal(RotateRefreshTokenResult.Success, (await RotateAsync(phone)).Result);
    }

    // ---------- RotateAsync: concurrency ----------

    [Fact]
    public async Task RotateAsync_ConcurrentRefreshesOfTheSameToken_ExactlyOneWins()
    {
        // Without the `revoked_at IS NULL` guard on the claim, every racer would rotate and the family would end up
        // with several live tokens. Each racer has its own context and connection, like separate HTTP requests.
        const int racers = 8;
        var user = await SeedUserAsync();
        var raw = await IssueAsync(user.Id);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, racers).Select(async _ =>
        {
            await gate.Task;
            return await RotateAsync(raw);
        }).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Single(results, r => r.Result == RotateRefreshTokenResult.Success);
        // The losers are inside the grace window or lost the claim: refused, never treated as theft.
        Assert.Equal(racers - 1, results.Count(r => r.Result == RotateRefreshTokenResult.Invalid));

        // The losers' inserted rows were rolled back: the old token plus the winner's new one.
        var rows = await GetUserRowsAsync(user.Id);
        Assert.Equal(2, rows.Count);
        var live = Assert.Single(rows, r => r.RevokedAt == null);
        var winner = results.Single(r => r.Result == RotateRefreshTokenResult.Success);
        Assert.Equal(Hash(winner.NewToken!), live.TokenHash);
    }

    // ---------- RevokeAsync ----------

    [Fact]
    public async Task RevokeAsync_ActiveToken_MarksItLoggedOut()
    {
        var user = await SeedUserAsync();
        var raw = await IssueAsync(user.Id);

        Assert.True(await RevokeAsync(raw));

        var row = await GetRowAsync(raw);
        Assert.Equal(RefreshTokenRevokedReason.Logout, row.RevokedReason);
        Assert.NotNull(row.RevokedAt);
    }

    [Fact]
    public async Task RevokeAsync_IsIdempotent_AndKeepsTheFirstRevocationTime()
    {
        var user = await SeedUserAsync();
        var raw = await IssueAsync(user.Id);
        await RevokeAsync(raw);
        var first = await GetRowAsync(raw);

        Assert.False(await RevokeAsync(raw));

        var again = await GetRowAsync(raw);
        Assert.Equal(first.RevokedAt, again.RevokedAt);
        Assert.Equal(RefreshTokenRevokedReason.Logout, again.RevokedReason);
    }

    [Fact]
    public async Task RevokeAsync_RotatedToken_KeepsItsReason_AndLeavesTheNewTokenActive()
    {
        // Logging out with a stale cookie must not rewrite history ("Rotated" is what reuse detection reads).
        var user = await SeedUserAsync();
        var raw = await IssueAsync(user.Id);
        var next = await RotateAsync(raw);

        Assert.False(await RevokeAsync(raw));

        Assert.Equal(RefreshTokenRevokedReason.Rotated, (await GetRowAsync(raw)).RevokedReason);
        Assert.Null((await GetRowAsync(next.NewToken!)).RevokedAt);
    }

    [Fact]
    public async Task RevokeAsync_UnknownToken_ReturnsFalse()
    {
        Assert.False(await RevokeAsync("not-a-token-anyone-was-issued"));
    }
}
