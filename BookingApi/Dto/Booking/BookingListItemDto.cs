using BookingApi.Data;

namespace BookingApi.Dto.Booking;

public record BookingListItemDto(int Id, int ResourceId, string ResourceName, DateTime StartTime, DateTime EndTime, BookingStatus Status, int UserId, string Username);
