using BookingApi.Dto;
using BookingApi.Dto.Auth;

namespace BookingApi.Service;

public interface IAuthService
{
    public Task<AuthResponseDto?> LoginAsync(LoginDto dto);
    public Task<AuthResponseDto?> RegisterAsync(RegisterDto dto);
}