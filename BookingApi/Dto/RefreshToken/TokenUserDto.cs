using BookingApi.Data;

namespace BookingApi.Dto.RefreshToken;

public record TokenUserDto(int UserId, UserRole Role, string Username);