using BookingApi.Data;

namespace BookingApi.Dto.Booking;

public record BookingResponseDto(int Id, DateTime StartTime, DateTime EndTime, int ResourceId, BookingStatus Status, int UserId);