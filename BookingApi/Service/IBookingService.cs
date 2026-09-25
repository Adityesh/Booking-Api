using BookingApi.Dto.Booking;

namespace BookingApi.Service;

public interface IBookingService
{
    public Task<(BookingCreationResult result, BookingResponseDto? booking)> CreateAsync(CreateBookingDto dto, int userId, CancellationToken token);
    public Task<BookingResponseDto?> GetByIdAsync(int id, bool isAdmin, int userId, CancellationToken token = default);

    public Task<BookingCancellationResult> CancelAsync(int id, int userId, bool isAdmin,
        CancellationToken token = default);
}