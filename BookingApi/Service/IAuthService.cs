using BookingApi.Dto;
using BookingApi.Dto.Auth;

namespace BookingApi.Service;

public interface IAuthService
{
    public Task<AuthSession?> LoginAsync(LoginDto dto, CancellationToken token);
    public Task<AuthSession?> RegisterAsync(RegisterDto dto, CancellationToken token);
    public Task<AuthSession?> RefreshAsync(string rawToken, CancellationToken token);
}