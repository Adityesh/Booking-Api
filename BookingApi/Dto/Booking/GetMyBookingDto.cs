using BookingApi.Data;

namespace BookingApi.Dto.Booking;

public record GetMyBookingDto(BookingStatus? Status, BookingTimeFrame? When, int Page = 1, int PageSize = 20);

public enum BookingTimeFrame
{
    Upcoming,
    Past
}