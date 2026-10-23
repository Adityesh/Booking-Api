namespace BookingApi.Dto.Auth;

public record AuthSession(AuthResponseDto Response, string RefreshToken, DateTime RefreshExpiresAt);