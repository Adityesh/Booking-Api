using BookingApi.Data;
using BookingApi.Dto.Booking;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Service;

public class BookingService(AppDbContext context) : IBookingService
{
    public async Task<(BookingCreationResult result, BookingResponseDto? booking)> CreateAsync(CreateBookingDto dto,
        int userId, CancellationToken token)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);

        try
        {
            var resource =
                await context.Resources.FromSqlInterpolated(
                        $"SELECT * FROM resources WHERE id = {dto.ResourceId} FOR UPDATE")
                    .FirstOrDefaultAsync(token);
            if (resource == null)
            {
                await transaction.RollbackAsync(token);
                return (BookingCreationResult.ResourceNotFound, null);
            }

            if (!resource.IsActive)
            {
                await transaction.RollbackAsync(token);
                return (BookingCreationResult.ResourceInactive, null);
            }

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
                var waitListCreated = await CreateWaitlistEntry(resource.Id, userId, dto.StartTime, dto.EndTime, token);
                if (!waitListCreated)
                {
                    await transaction.RollbackAsync(token);
                    return (BookingCreationResult.AlreadyWaitListed, null);
                }

                await transaction.CommitAsync(token);
                return (BookingCreationResult.WaitListed, null);
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
            await transaction.CommitAsync(token);

            return (BookingCreationResult.Success,
                new BookingResponseDto(newBooking.Id, newBooking.StartTime, newBooking.EndTime, newBooking.ResourceId,
                    newBooking.Status, newBooking.UserId));
        }
        catch
        {
            await transaction.RollbackAsync(token);
            throw;
        }
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
        await using var transaction = await context.Database.BeginTransactionAsync(token);

        try
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

            await PromoteWaitList(booking.ResourceId, token);

            await transaction.CommitAsync(token);
            return BookingCancellationResult.Success;

        }
        catch (Exception)
        {
            await transaction.RollbackAsync(token);
            throw;
        }
    }

    private async Task PromoteWaitList(int resourceId, CancellationToken token)
    {
        var resource = await context.Resources
            .FromSqlInterpolated($"SELECT * FROM resources WHERE id = {resourceId} FOR UPDATE")
            .FirstOrDefaultAsync(token);

        if (resource is not { IsActive: true }) return;

        var waitlistEntries = await context.WaitlistEntries
            .Where(w => w.ResourceId == resourceId && w.Status == WaitlistStatus.Waiting && w.RequestedStartTime > DateTime.UtcNow)
            .OrderBy(w => w.CreatedAt)
            .ToListAsync(token);

        foreach (var entry in waitlistEntries)
        {
            var overlappingCount = await context.Bookings
                .Where(b => b.Status != BookingStatus.Cancelled
                            && b.ResourceId == resource.Id
                            && entry.RequestedEndTime > b.StartTime && entry.RequestedStartTime < b.EndTime)
                .CountAsync(token);

            if (overlappingCount >= resource.Capacity) continue;
            var booking = new BookingEntity()
            {
                StartTime = entry.RequestedStartTime,
                EndTime = entry.RequestedEndTime,
                ResourceId = resourceId,
                UserId = entry.UserId,
                Status = BookingStatus.Confirmed
            };

            context.Bookings.Add(booking);

            entry.Status = WaitlistStatus.Promoted;
            await context.SaveChangesAsync(token);
        }
    }

    private async Task<bool> CreateWaitlistEntry(int resourceId, int userId, DateTime startTime, DateTime endTime,
        CancellationToken token)
    {
        var waitListExists = await context.WaitlistEntries
            .AnyAsync(w =>
                w.ResourceId == resourceId && w.RequestedStartTime == startTime && w.RequestedEndTime == endTime &&
                w.UserId == userId &&
                w.Status == WaitlistStatus.Waiting , token);

        if (waitListExists) return false;

        var waitListEntry = new WaitlistEntryEntity()
        {
            RequestedEndTime = endTime,
            RequestedStartTime = startTime,
            ResourceId = resourceId,
            Status = WaitlistStatus.Waiting,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        context.WaitlistEntries.Add(waitListEntry);
        await context.SaveChangesAsync(token);
        return true;
    }
}