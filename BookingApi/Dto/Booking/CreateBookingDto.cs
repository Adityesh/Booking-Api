namespace BookingApi.Dto.Booking;

public record CreateBookingDto(DateTime StartTime, DateTime EndTime, int ResourceId);