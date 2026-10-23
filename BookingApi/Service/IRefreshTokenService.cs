using BookingApi.Dto.RefreshToken;

namespace BookingApi.Service;

public interface IRefreshTokenService
{
    public Task<(DateTime ExpiresAt, string RawToken)> IssueAsync(int userId, CancellationToken token);
    public Task<RotateRefreshTokenResponseDto> RotateAsync(string rawToken, CancellationToken token);
    public Task<bool> RevokeAsync(string rawToken, CancellationToken token);
}