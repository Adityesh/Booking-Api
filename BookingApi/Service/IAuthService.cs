using BookingApi.Dto;
using BookingApi.Dto.Auth;

namespace BookingApi.Service;

public interface IAuthService
{
    public Task<AuthResponseDto?> LoginAsync(LoginDto dto, CancellationToken token);
    public Task<AuthResponseDto?> RegisterAsync(RegisterDto dto, CancellationToken token);
}