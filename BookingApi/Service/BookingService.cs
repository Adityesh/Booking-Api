using BookingApi.Data;
using BookingApi.Dto.AuditLog;
using BookingApi.Dto.Booking;
using BookingApi.Dto.Waitlist;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Service;

public class BookingService(AppDbContext context, IAuditLogService auditLogService) : IBookingService
{
    public async Task<BookingCreationOutcome> CreateAsync(CreateBookingDto dto,
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
                return new BookingCreationOutcome(BookingCreationResult.ResourceNotFound, null, null);
            }

            if (!resource.IsActive)
            {
                await transaction.RollbackAsync(token);
                return new BookingCreationOutcome(BookingCreationResult.ResourceInactive, null, null);
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
                var waitlistEntry = await CreateWaitlistEntry(resource.Id, userId, dto.StartTime, dto.EndTime, token);
                if (waitlistEntry == null)
                {
                    await transaction.RollbackAsync(token);
                    return new BookingCreationOutcome(BookingCreationResult.AlreadyWaitListed, null, null);
                }

                await transaction.CommitAsync(token);
                return new BookingCreationOutcome(BookingCreationResult.WaitListed, null, waitlistEntry);
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
            auditLogService.CreateLog(
                new CreateAuditLogDto(newBooking.Id, newBooking.UserId, ActionType.BookingCreated));
            await context.SaveChangesAsync(token);
            await transaction.CommitAsync(token);

            return new BookingCreationOutcome(BookingCreationResult.Success,
                new BookingResponseDto(newBooking.Id, newBooking.StartTime, newBooking.EndTime, newBooking.ResourceId,
                    newBooking.Status, newBooking.UserId), null);
        }
        catch
        {
            await transaction.RollbackAsync(token);
            throw;
        }
    }

    public async Task<BookingResponseDto?> GetByIdAsync(int id, bool isAdmin, int userId,
        CancellationToken token = default)
    {
        var baseQuery = context.Bookings.Where(b => b.Id == id).AsQueryable();

        if (!isAdmin) baseQuery = baseQuery.Where(b => b.UserId == userId);
        var booking = await baseQuery.FirstOrDefaultAsync(token);

        return booking == null
            ? null
            : new BookingResponseDto(booking.Id, booking.StartTime, booking.EndTime, booking.ResourceId,
                booking.Status, booking.UserId);
    }

    public async Task<BookingCancellationResult> CancelAsync(int id, int userId, bool isAdmin,
        CancellationToken token = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);

        try
        {
            // 1. Look up the booking WITHOUT tracking it and make no decisions from it yet.
            //    This only answers "does it exist / is it mine" and "which resource do I need to lock".
            var lookup = context.Bookings.AsNoTracking().Where(b => b.Id == id);
            if (!isAdmin) lookup = lookup.Where(b => b.UserId == userId);

            var resourceId = await lookup.Select(b => (int?)b.ResourceId).FirstOrDefaultAsync(token);
            if (resourceId == null) return BookingCancellationResult.NotFound;

            // 2. Take the same resource lock that CreateAsync uses, BEFORE reading the status.
            //    A concurrent cancel of this booking (or of any booking on this resource) now waits here.
            var resource = await context.Resources
                .FromSqlInterpolated($"SELECT * FROM resources WHERE id = {resourceId} FOR UPDATE")
                .FirstAsync(token);

            // 3. Read the booking again, tracked this time. Step 1 left it untracked, so this is a fresh read
            //    that sees whatever the previous lock holder committed.
            var booking = await context.Bookings.FirstAsync(b => b.Id == id, token);

            if (booking.Status == BookingStatus.Cancelled) return BookingCancellationResult.AlreadyCancelled;

            if (!isAdmin && booking.StartTime <= DateTime.UtcNow.AddHours(2))
            {
                return BookingCancellationResult.TooCloseToStartTime;
            }

            booking.Status = BookingStatus.Cancelled;
            auditLogService.CreateLog(new CreateAuditLogDto(booking.Id, userId, ActionType.BookingCancelled));
            await context.SaveChangesAsync(token);

            await PromoteWaitList(resource, token);

            await transaction.CommitAsync(token);
            return BookingCancellationResult.Success;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(token);
            throw;
        }
    }

    private async Task PromoteWaitList(ResourceEntity resource, CancellationToken token)
    {
        // The caller already holds the FOR UPDATE lock on this resource row.
        if (!resource.IsActive) return;

        var waitlistEntries = await context.WaitlistEntries
            .Where(w => w.ResourceId == resource.Id && w.Status == WaitlistStatus.Waiting &&
                        w.RequestedStartTime > DateTime.UtcNow)
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
                ResourceId = resource.Id,
                UserId = entry.UserId,
                Status = BookingStatus.Confirmed
            };

            context.Bookings.Add(booking);
            await context.SaveChangesAsync(token);
            auditLogService.CreateLog(new CreateAuditLogDto(booking.Id, booking.UserId, ActionType.BookingCreated));

            entry.Status = WaitlistStatus.Promoted;
            auditLogService.CreateLog(new CreateAuditLogDto(entry.Id, entry.UserId, ActionType.WaitlistEntryPromoted));
            await context.SaveChangesAsync(token);
        }
    }

    private async Task<WaitlistEntryResponseDto?> CreateWaitlistEntry(int resourceId, int userId, DateTime startTime, DateTime endTime,
        CancellationToken token)
    {
        var waitListExists = await context.WaitlistEntries
            .AnyAsync(w =>
                w.ResourceId == resourceId && w.RequestedStartTime == startTime && w.RequestedEndTime == endTime &&
                w.UserId == userId &&
                w.Status == WaitlistStatus.Waiting, token);

        if (waitListExists) return null;

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
        auditLogService.CreateLog(new CreateAuditLogDto(waitListEntry.Id, userId, ActionType.WaitlistEntryCreated));
        await context.SaveChangesAsync(token);
        return new WaitlistEntryResponseDto(waitListEntry.Id, waitListEntry.ResourceId, waitListEntry.RequestedStartTime, waitListEntry.RequestedEndTime, waitListEntry.Status, waitListEntry.CreatedAt);
    }
}