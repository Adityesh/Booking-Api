using BookingApi.Data;
using BookingApi.Dto.Booking;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Service;

public class BookingService(AppDbContext context) : IBookingService
{
    public async Task<(BookingCreationResult result, BookingResponseDto? booking)> CreateAsync(CreateBookingDto dto,
        int userId, CancellationToken token)
    {
        var resource = await context.Resources.FirstOrDefaultAsync(b => b.Id == dto.ResourceId, token);
        if (resource == null) return (BookingCreationResult.ResourceNotFound, null);
        if (!resource.IsActive) return (BookingCreationResult.ResourceInactive, null);

        // when will the bookings never overlap
        // new booking S1 and E1
        // existing booking S2 and E2
        // condition -> S2 >= E1 || E2 >= S1
        // so overlapping will be negation ->  !(S2 >= E1 || E2 <= S1) => E1 > S2 && S1 < E2
        var overlappingCounts = await context.Bookings.Where(b =>
                b.Status != BookingStatus.Cancelled
                && b.ResourceId == resource.Id
                && dto.EndTime > b.StartTime && dto.StartTime < b.EndTime)
            .CountAsync(token);

        if (overlappingCounts >= resource.Capacity)
        {
            return (BookingCreationResult.NoCapacity, null);
        }

        var newBooking = new BookingEntity
        {
            ResourceId = resource.Id,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            Status = BookingStatus.Confirmed,
            UserId = userId,
        };

        context.Bookings.Add(newBooking);
        await context.SaveChangesAsync(token);

        return (BookingCreationResult.Success,
            new BookingResponseDto(newBooking.Id, newBooking.StartTime, newBooking.EndTime, newBooking.ResourceId,
                newBooking.Status, newBooking.UserId));
    }

    public async Task<BookingResponseDto?> GetByIdAsync(int id, bool isAdmin, int userId, CancellationToken token = default)
    {
        var baseQuery = context.Bookings.Where(b => b.Id == id).AsQueryable();

        if (!isAdmin) baseQuery = baseQuery.Where(b => b.UserId == userId);
        var booking = await baseQuery.FirstOrDefaultAsync(token);

        return booking == null
            ? null
            : new BookingResponseDto(booking.Id, booking.StartTime, booking.EndTime, booking.ResourceId,
                booking.Status, booking.UserId);
    }

    public async Task<BookingCancellationResult> CancelAsync(int id, int userId, bool isAdmin, CancellationToken token = default)
    {
        var baseQuery = context.Bookings.Where(b => b.Id == id).AsQueryable();

        if (!isAdmin) baseQuery = baseQuery.Where(b => b.UserId == userId);

        var booking = await baseQuery.FirstOrDefaultAsync(token);
        if (booking == null) return BookingCancellationResult.NotFound;

        if (booking.Status == BookingStatus.Cancelled) return BookingCancellationResult.AlreadyCancelled;

        if (!isAdmin && booking.StartTime <= DateTime.UtcNow.AddHours(2))
        {
            return BookingCancellationResult.TooCloseToStartTime;
        }

        booking.Status = BookingStatus.Cancelled;
        await context.SaveChangesAsync(token);
        return BookingCancellationResult.Success;
    }
}