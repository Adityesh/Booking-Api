using BookingApi.Data;

namespace BookingApi.Dto;

public record AuthResponseDto(string Token, string Username, UserRole Role, DateTime ExpiresAt);