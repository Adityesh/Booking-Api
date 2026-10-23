using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using BookingApi.Data;
using BookingApi.Dto.RefreshToken;
using BookingApi.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BookingApi.Service;

public class RefreshTokenService(AppDbContext context, IOptions<RefreshTokenOptions> refreshOptions, ILogger<RefreshTokenService> logger) : IRefreshTokenService
{
    public async Task<(DateTime ExpiresAt, string RawToken)> IssueAsync(int userId, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        var rawToken = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var hashedToken = HashToken(rawToken);

        var newRefreshToken = new RefreshTokenEntity()
        {
            TokenHash = hashedToken,
            FamilyId = Guid.NewGuid(),
            ExpiresAt = now.AddDays(refreshOptions.Value.LifetimeDays),
            AbsoluteExpiresAt = now.AddDays(refreshOptions.Value.AbsoluteLifetimeDays),
            CreatedAt = now,
            UserId = userId
        };

        context.RefreshTokens.Add(newRefreshToken);
        await context.SaveChangesAsync(token);

        return (newRefreshToken.ExpiresAt, rawToken);
    }

    public async Task<RotateRefreshTokenResponseDto> RotateAsync(string rawToken, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        var hashedToken = HashToken(rawToken);
        var oldToken = await context.RefreshTokens
            .Where(r => r.TokenHash == hashedToken && r.User.IsActive)
            .Include(r => r.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(token);

        if (oldToken == null) return new RotateRefreshTokenResponseDto(RotateRefreshTokenResult.Invalid);

        // Revoked is checked before expiry: replaying a rotated token is evidence of theft however old it is, and its
        // descendant may still be live.
        if (oldToken.RevokedAt != null)
        {
            if (oldToken.RevokedReason != RefreshTokenRevokedReason.Rotated)
                return new RotateRefreshTokenResponseDto(RotateRefreshTokenResult.Invalid);

            // Rotated moments ago: a second tab or a retry lost the race. Refuse it, revoke nothing.
            if ((now - oldToken.RevokedAt.Value).TotalSeconds <= refreshOptions.Value.ReuseGraceSeconds)
                return new RotateRefreshTokenResponseDto(RotateRefreshTokenResult.Invalid);

            // Rotated a while ago: someone is replaying it. Kill whichever token of the family is live.
            await context.RefreshTokens
                .Where(r => r.FamilyId == oldToken.FamilyId && r.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.RevokedAt, now)
                    .SetProperty(r => r.RevokedReason, RefreshTokenRevokedReason.ReuseDetected), token);
            logger.LogWarning("Refresh token reuse detected for user {UserId}, family {FamilyId}", oldToken.UserId,
                oldToken.FamilyId);
            return new RotateRefreshTokenResponseDto(RotateRefreshTokenResult.ReuseDetected);
        }

        if (oldToken.ExpiresAt <= now || oldToken.AbsoluteExpiresAt <= now)
            return new RotateRefreshTokenResponseDto(RotateRefreshTokenResult.Invalid);

        // Active: rotate it.
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        try
        {
            var newRawToken = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            var newHashToken = HashToken(newRawToken);
            var newExpiresAt = now.AddDays(refreshOptions.Value.LifetimeDays);
            var newToken = new RefreshTokenEntity()
            {
                FamilyId = oldToken.FamilyId,
                AbsoluteExpiresAt = oldToken.AbsoluteExpiresAt,
                ExpiresAt = newExpiresAt < oldToken.AbsoluteExpiresAt ? newExpiresAt : oldToken.AbsoluteExpiresAt,
                CreatedAt = now,
                TokenHash = newHashToken,
                UserId = oldToken.UserId,
            };

            context.RefreshTokens.Add(newToken);
            await context.SaveChangesAsync(token);

            var updateCount = await context.RefreshTokens
                .Where(r => r.Id == oldToken.Id && r.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.RevokedAt, now)
                    .SetProperty(r => r.RevokedReason, RefreshTokenRevokedReason.Rotated)
                    .SetProperty(r => r.ReplacedById, newToken.Id), token);

            if (updateCount == 0)
            {
                await transaction.RollbackAsync(token);
                return new RotateRefreshTokenResponseDto(RotateRefreshTokenResult.Invalid);
            }

            await transaction.CommitAsync(token);
            return new RotateRefreshTokenResponseDto(
                RotateRefreshTokenResult.Success,
                NewToken: newRawToken,
                ExpiresAt: newToken.ExpiresAt,
                TokenUser: new TokenUserDto(oldToken.User.Id, oldToken.User.Role, oldToken.User.Username));
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(token);
            throw;
        }

    }

    public async Task<bool> RevokeAsync(string rawToken, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        var hashedToken = HashToken(rawToken);
        var result = await context.RefreshTokens
            .Where(r => r.RevokedAt == null && r.TokenHash == hashedToken)
            .ExecuteUpdateAsync(setters =>
                setters
                    .SetProperty(b => b.RevokedAt, now)
                    .SetProperty(b => b.RevokedReason, RefreshTokenRevokedReason.Logout), token);

        return result > 0;
    }

    private static string HashToken(string rawToken)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }
}