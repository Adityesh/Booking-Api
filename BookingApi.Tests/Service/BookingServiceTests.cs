using BookingApi.Data;
using BookingApi.Dto.Booking;
using BookingApi.Dto.Waitlist;
using BookingApi.Service;
using BookingApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Tests.Service;

[Collection("Postgres")]
public class BookingServiceTests(PostgresFixture fixture)
{
    // Far enough ahead that a non-admin can cancel (2h rule) and a waitlist entry is still promotable.
    private static readonly DateTime Slot = DateTime.UtcNow.Date.AddDays(3).AddHours(10);

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options);
    }

    // The real audit service on the SAME context, exactly as DI wires it per request.
    private static BookingService BookingServiceFor(AppDbContext context) => new(context, new AuditLogService(context));
    private static WaitlistService WaitlistServiceFor(AppDbContext context) => new(context, new AuditLogService(context));

    // ---------- seeding (every test gets a fresh resource and fresh users, so tests never interfere) ----------

    private async Task<int> SeedUserAsync(UserRole role = UserRole.User)
    {
        await using var context = CreateContext();
        var user = new UserEntity
        {
            Username = $"u_{Guid.NewGuid():N}",
            PasswordHash = "not-a-real-hash",
            Role = role,
            IsActive = true
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task<int> SeedResourceAsync(int capacity)
    {
        await using var context = CreateContext();
        var resource = new ResourceEntity
        {
            Name = "Audit test resource",
            Type = ResourceType.Room,
            Capacity = capacity,
            IsActive = true
        };
        context.Resources.Add(resource);
        await context.SaveChangesAsync();
        return resource.Id;
    }

    private async Task<int> SeedBookingAsync(int resourceId, int userId, DateTime start, DateTime end)
    {
        await using var context = CreateContext();
        var booking = new BookingEntity
        {
            ResourceId = resourceId,
            UserId = userId,
            StartTime = start,
            EndTime = end,
            Status = BookingStatus.Confirmed
        };
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();
        return booking.Id;
    }

    private async Task<int> SeedWaitlistEntryAsync(int resourceId, int userId, DateTime start, DateTime end,
        DateTime createdAt, WaitlistStatus status = WaitlistStatus.Waiting)
    {
        await using var context = CreateContext();
        var entry = new WaitlistEntryEntity
        {
            ResourceId = resourceId,
            UserId = userId,
            RequestedStartTime = start,
            RequestedEndTime = end,
            Status = status,
            CreatedAt = createdAt
        };
        context.WaitlistEntries.Add(entry);
        await context.SaveChangesAsync();
        return entry.Id;
    }

    // ---------- reading ----------

    // EntityId is shared across entity types (a booking and a waitlist entry can both have id 7),
    // so entries are always matched on the action as well as the id.
    private async Task<List<AuditLogEntryEntity>> EntriesAsync(ActionType action, int entityId)
    {
        await using var context = CreateContext();
        return await context.AuditLogEntries.AsNoTracking()
            .Where(e => e.Action == action && e.EntityId == entityId)
            .ToListAsync();
    }

    private async Task<List<AuditLogEntryEntity>> EntriesByUserAsync(int userId)
    {
        await using var context = CreateContext();
        return await context.AuditLogEntries.AsNoTracking().Where(e => e.UserId == userId).ToListAsync();
    }

    private async Task<List<int>> BookingIdsAsync(int resourceId, int userId)
    {
        await using var context = CreateContext();
        return await context.Bookings.AsNoTracking()
            .Where(b => b.ResourceId == resourceId && b.UserId == userId)
            .Select(b => b.Id)
            .ToListAsync();
    }

    private async Task<int> WaitlistEntryIdAsync(int resourceId, int userId)
    {
        await using var context = CreateContext();
        return await context.WaitlistEntries.AsNoTracking()
            .Where(w => w.ResourceId == resourceId && w.UserId == userId)
            .Select(w => w.Id)
            .SingleAsync();
    }

    // ======================= Create =======================

    [Fact]
    public async Task CreateAsync_Success_LogsBookingCreatedWithBookingIdAndUser()
    {
        var userId = await SeedUserAsync();
        var resourceId = await SeedResourceAsync(capacity: 1);

        (BookingCreationResult result, BookingResponseDto? booking) outcome;
        await using (var context = CreateContext())
        {
            outcome = await BookingServiceFor(context)
                .CreateAsync(new CreateBookingDto(Slot, Slot.AddHours(1), resourceId), userId, CancellationToken.None);
        }

        Assert.Equal(BookingCreationResult.Success, outcome.result);
        var bookingId = Assert.Single(await BookingIdsAsync(resourceId, userId));
        var entry = Assert.Single(await EntriesAsync(ActionType.BookingCreated, bookingId));
        Assert.Equal(userId, entry.UserId);
    }

    [Fact]
    public async Task CreateAsync_ResourceFull_WaitListsAndLogsWaitlistEntryCreated_ButNoBookingCreated()
    {
        var holderId = await SeedUserAsync();
        var userId = await SeedUserAsync();
        var resourceId = await SeedResourceAsync(capacity: 1);
        await SeedBookingAsync(resourceId, holderId, Slot, Slot.AddHours(1));

        (BookingCreationResult result, BookingResponseDto? booking) outcome;
        await using (var context = CreateContext())
        {
            outcome = await BookingServiceFor(context)
                .CreateAsync(new CreateBookingDto(Slot, Slot.AddHours(1), resourceId), userId, CancellationToken.None);
        }

        Assert.Equal(BookingCreationResult.WaitListed, outcome.result);
        var entryId = await WaitlistEntryIdAsync(resourceId, userId);
        var logged = Assert.Single(await EntriesAsync(ActionType.WaitlistEntryCreated, entryId));
        Assert.Equal(userId, logged.UserId);
        Assert.DoesNotContain(await EntriesByUserAsync(userId), e => e.Action == ActionType.BookingCreated);
    }

    [Fact]
    public async Task CreateAsync_AlreadyWaitListed_DoesNotLogASecondEntry()
    {
        var holderId = await SeedUserAsync();
        var userId = await SeedUserAsync();
        var resourceId = await SeedResourceAsync(capacity: 1);
        await SeedBookingAsync(resourceId, holderId, Slot, Slot.AddHours(1));
        var dto = new CreateBookingDto(Slot, Slot.AddHours(1), resourceId);

        (BookingCreationResult result, BookingResponseDto? booking) first, second;
        await using (var context = CreateContext())
            first = await BookingServiceFor(context).CreateAsync(dto, userId, CancellationToken.None);
        await using (var context = CreateContext())
            second = await BookingServiceFor(context).CreateAsync(dto, userId, CancellationToken.None);

        Assert.Equal(BookingCreationResult.WaitListed, first.result);
        Assert.Equal(BookingCreationResult.AlreadyWaitListed, second.result);
        var entryId = await WaitlistEntryIdAsync(resourceId, userId);
        Assert.Single(await EntriesAsync(ActionType.WaitlistEntryCreated, entryId));
        Assert.Single(await EntriesByUserAsync(userId));
    }

    [Fact]
    public async Task CreateAsync_UnknownResource_LogsNothing()
    {
        var userId = await SeedUserAsync();

        (BookingCreationResult result, BookingResponseDto? booking) outcome;
        await using (var context = CreateContext())
        {
            outcome = await BookingServiceFor(context)
                .CreateAsync(new CreateBookingDto(Slot, Slot.AddHours(1), int.MaxValue), userId, CancellationToken.None);
        }

        Assert.Equal(BookingCreationResult.ResourceNotFound, outcome.result);
        Assert.Empty(await EntriesByUserAsync(userId));
    }

    // ======================= Cancel =======================

    [Fact]
    public async Task CancelAsync_ByOwner_LogsBookingCancelledWithOwnerAsActor()
    {
        var ownerId = await SeedUserAsync();
        var resourceId = await SeedResourceAsync(capacity: 1);
        var bookingId = await SeedBookingAsync(resourceId, ownerId, Slot, Slot.AddHours(1));

        BookingCancellationResult result;
        await using (var context = CreateContext())
            result = await BookingServiceFor(context).CancelAsync(bookingId, ownerId, isAdmin: false, CancellationToken.None);

        Assert.Equal(BookingCancellationResult.Success, result);
        var entry = Assert.Single(await EntriesAsync(ActionType.BookingCancelled, bookingId));
        Assert.Equal(ownerId, entry.UserId);
    }

    [Fact]
    public async Task CancelAsync_ByAdminOnSomeoneElsesBooking_LogsTheAdminAsActor()
    {
        var ownerId = await SeedUserAsync();
        var adminId = await SeedUserAsync(UserRole.Admin);
        var resourceId = await SeedResourceAsync(capacity: 1);
        var bookingId = await SeedBookingAsync(resourceId, ownerId, Slot, Slot.AddHours(1));

        BookingCancellationResult result;
        await using (var context = CreateContext())
            result = await BookingServiceFor(context).CancelAsync(bookingId, adminId, isAdmin: true, CancellationToken.None);

        Assert.Equal(BookingCancellationResult.Success, result);
        var entry = Assert.Single(await EntriesAsync(ActionType.BookingCancelled, bookingId));
        Assert.Equal(adminId, entry.UserId);   // the one who acted, not the booking's owner
    }

    [Fact]
    public async Task CancelAsync_AlreadyCancelled_DoesNotLogASecondEntry()
    {
        var ownerId = await SeedUserAsync();
        var resourceId = await SeedResourceAsync(capacity: 1);
        var bookingId = await SeedBookingAsync(resourceId, ownerId, Slot, Slot.AddHours(1));

        BookingCancellationResult first, second;
        await using (var context = CreateContext())
            first = await BookingServiceFor(context).CancelAsync(bookingId, ownerId, false, CancellationToken.None);
        await using (var context = CreateContext())
            second = await BookingServiceFor(context).CancelAsync(bookingId, ownerId, false, CancellationToken.None);

        Assert.Equal(BookingCancellationResult.Success, first);
        Assert.Equal(BookingCancellationResult.AlreadyCancelled, second);
        Assert.Single(await EntriesAsync(ActionType.BookingCancelled, bookingId));
    }

    [Fact]
    public async Task CancelAsync_TooCloseToStart_NonAdmin_LogsNothing()
    {
        var ownerId = await SeedUserAsync();
        var resourceId = await SeedResourceAsync(capacity: 1);
        var soon = DateTime.UtcNow.AddHours(1);
        var bookingId = await SeedBookingAsync(resourceId, ownerId, soon, soon.AddHours(1));

        BookingCancellationResult result;
        await using (var context = CreateContext())
            result = await BookingServiceFor(context).CancelAsync(bookingId, ownerId, false, CancellationToken.None);

        Assert.Equal(BookingCancellationResult.TooCloseToStartTime, result);
        Assert.Empty(await EntriesAsync(ActionType.BookingCancelled, bookingId));
    }

    // ======================= Promotion =======================

    [Fact]
    public async Task CancelAsync_PromotingAWaitingUser_LogsCancelled_Promoted_AndBookingCreatedForThePromotedUser()
    {
        var ownerId = await SeedUserAsync();
        var waiterId = await SeedUserAsync();
        var resourceId = await SeedResourceAsync(capacity: 1);
        var bookingId = await SeedBookingAsync(resourceId, ownerId, Slot, Slot.AddHours(1));
        var waitlistId = await SeedWaitlistEntryAsync(resourceId, waiterId, Slot, Slot.AddHours(1), DateTime.UtcNow);

        await using (var context = CreateContext())
            await BookingServiceFor(context).CancelAsync(bookingId, ownerId, false, CancellationToken.None);

        var cancelled = Assert.Single(await EntriesAsync(ActionType.BookingCancelled, bookingId));
        Assert.Equal(ownerId, cancelled.UserId);

        var promoted = Assert.Single(await EntriesAsync(ActionType.WaitlistEntryPromoted, waitlistId));
        Assert.Equal(waiterId, promoted.UserId);

        var newBookingId = Assert.Single(await BookingIdsAsync(resourceId, waiterId));
        var created = Assert.Single(await EntriesAsync(ActionType.BookingCreated, newBookingId));
        Assert.Equal(waiterId, created.UserId);
    }

    [Fact]
    public async Task CancelAsync_FreeingRoomForTwoWaiters_LogsBothPromotions_IncludingTheLastIteration()
    {
        // Capacity 1, owner holds 10:00-12:00. Two waiters want 10:00-11:00 and 11:00-12:00:
        // both fit in the freed slot, so one cancellation promotes both. The last promotion's
        // entries are the ones that go missing if the final iteration forgets to save.
        var ownerId = await SeedUserAsync();
        var firstWaiterId = await SeedUserAsync();
        var secondWaiterId = await SeedUserAsync();
        var resourceId = await SeedResourceAsync(capacity: 1);
        var bookingId = await SeedBookingAsync(resourceId, ownerId, Slot, Slot.AddHours(2));
        var now = DateTime.UtcNow;
        var firstEntryId = await SeedWaitlistEntryAsync(resourceId, firstWaiterId, Slot, Slot.AddHours(1), now);
        var secondEntryId = await SeedWaitlistEntryAsync(resourceId, secondWaiterId, Slot.AddHours(1), Slot.AddHours(2), now.AddSeconds(1));

        await using (var context = CreateContext())
            await BookingServiceFor(context).CancelAsync(bookingId, ownerId, false, CancellationToken.None);

        Assert.Single(await EntriesAsync(ActionType.BookingCancelled, bookingId));
        Assert.Equal(firstWaiterId, Assert.Single(await EntriesAsync(ActionType.WaitlistEntryPromoted, firstEntryId)).UserId);
        Assert.Equal(secondWaiterId, Assert.Single(await EntriesAsync(ActionType.WaitlistEntryPromoted, secondEntryId)).UserId);

        var firstBookingId = Assert.Single(await BookingIdsAsync(resourceId, firstWaiterId));
        var secondBookingId = Assert.Single(await BookingIdsAsync(resourceId, secondWaiterId));
        Assert.Single(await EntriesAsync(ActionType.BookingCreated, firstBookingId));
        Assert.Single(await EntriesAsync(ActionType.BookingCreated, secondBookingId));
    }

    [Fact]
    public async Task CancelAsync_WithNobodyWaiting_LogsOnlyTheCancellation()
    {
        var ownerId = await SeedUserAsync();
        var resourceId = await SeedResourceAsync(capacity: 1);
        var bookingId = await SeedBookingAsync(resourceId, ownerId, Slot, Slot.AddHours(1));

        await using (var context = CreateContext())
            await BookingServiceFor(context).CancelAsync(bookingId, ownerId, false, CancellationToken.None);

        var entry = Assert.Single(await EntriesByUserAsync(ownerId));
        Assert.Equal(ActionType.BookingCancelled, entry.Action);
    }

    // ======================= Withdraw =======================

    [Fact]
    public async Task WithdrawAsync_WaitingEntry_LogsWaitlistEntryWithdrawn()
    {
        var userId = await SeedUserAsync();
        var resourceId = await SeedResourceAsync(capacity: 1);
        var entryId = await SeedWaitlistEntryAsync(resourceId, userId, Slot, Slot.AddHours(1), DateTime.UtcNow);

        WithdrawWaitlistResult result;
        await using (var context = CreateContext())
            result = await WaitlistServiceFor(context).WithdrawAsync(entryId, userId, CancellationToken.None);

        Assert.Equal(WithdrawWaitlistResult.Success, result);
        var entry = Assert.Single(await EntriesAsync(ActionType.WaitlistEntryWithdrawn, entryId));
        Assert.Equal(userId, entry.UserId);
    }

    [Fact]
    public async Task WithdrawAsync_CalledTwice_IsIdempotent_AndLogsOnce()
    {
        var userId = await SeedUserAsync();
        var resourceId = await SeedResourceAsync(capacity: 1);
        var entryId = await SeedWaitlistEntryAsync(resourceId, userId, Slot, Slot.AddHours(1), DateTime.UtcNow);

        WithdrawWaitlistResult first, second;
        await using (var context = CreateContext())
            first = await WaitlistServiceFor(context).WithdrawAsync(entryId, userId, CancellationToken.None);
        await using (var context = CreateContext())
            second = await WaitlistServiceFor(context).WithdrawAsync(entryId, userId, CancellationToken.None);

        Assert.Equal(WithdrawWaitlistResult.Success, first);
        Assert.Equal(WithdrawWaitlistResult.Success, second);
        Assert.Single(await EntriesAsync(ActionType.WaitlistEntryWithdrawn, entryId));
    }

    [Fact]
    public async Task WithdrawAsync_AlreadyPromotedEntry_ReturnsNotWaiting_AndLogsNothing()
    {
        var userId = await SeedUserAsync();
        var resourceId = await SeedResourceAsync(capacity: 1);
        var entryId = await SeedWaitlistEntryAsync(resourceId, userId, Slot, Slot.AddHours(1), DateTime.UtcNow, WaitlistStatus.Promoted);

        WithdrawWaitlistResult result;
        await using (var context = CreateContext())
            result = await WaitlistServiceFor(context).WithdrawAsync(entryId, userId, CancellationToken.None);

        Assert.Equal(WithdrawWaitlistResult.NotWaiting, result);
        Assert.Empty(await EntriesAsync(ActionType.WaitlistEntryWithdrawn, entryId));
    }

    [Fact]
    public async Task WithdrawAsync_SomeoneElsesEntry_ReturnsNotFound_AndLogsNothing()
    {
        var ownerId = await SeedUserAsync();
        var strangerId = await SeedUserAsync();
        var resourceId = await SeedResourceAsync(capacity: 1);
        var entryId = await SeedWaitlistEntryAsync(resourceId, ownerId, Slot, Slot.AddHours(1), DateTime.UtcNow);

        WithdrawWaitlistResult result;
        await using (var context = CreateContext())
            result = await WaitlistServiceFor(context).WithdrawAsync(entryId, strangerId, CancellationToken.None);

        Assert.Equal(WithdrawWaitlistResult.NotFound, result);
        Assert.Empty(await EntriesAsync(ActionType.WaitlistEntryWithdrawn, entryId));
    }
}