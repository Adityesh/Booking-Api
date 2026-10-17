using BookingApi.Data;

namespace BookingApi.Dto.Booking;

public record GetAdminBookingDto(int? UserId, int? ResourceId, BookingStatus? Status, DateTimeOffset? From, DateTimeOffset? To, int Page = 1, int PageSize = 20);