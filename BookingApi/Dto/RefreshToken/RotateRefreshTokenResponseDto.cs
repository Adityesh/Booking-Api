using BookingApi.Data;

namespace BookingApi.Dto.RefreshToken;

public record RotateRefreshTokenResponseDto(RotateRefreshTokenResult Result, string? NewToken = null, DateTime? ExpiresAt = null, TokenUserDto? TokenUser = null);