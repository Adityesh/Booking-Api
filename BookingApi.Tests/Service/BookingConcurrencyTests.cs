using BookingApi.Data;
using BookingApi.Dto.Booking;
using BookingApi.Service;
using BookingApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Tests.Service;

// Everything in BookingWaitlistTests runs one request at a time. The reason FOR UPDATE exists is
// concurrent requests, so these tests fire real parallel requests (each with its own DbContext and
// connection, like separate HTTP requests) and check the invariants that locking is supposed to protect.
[Collection("Postgres")]
public class BookingConcurrencyTests(PostgresFixture fixture)
{
    private readonly DateTime _day = DateTime.UtcNow.Date.AddDays(10);

    private DateTime At(int hour) => _day.AddHours(hour);

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options);
    }

    private async Task<UserEntity> SeedUserAsync()
    {
        await using var context = CreateContext();
        var user = new UserEntity
        {
            Username = $"user_{Guid.NewGuid():N}",
            PasswordHash = "not-a-real-hash",
            Role = UserRole.User,
            IsActive = true
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private async Task<ResourceEntity> SeedResourceAsync(int capacity)
    {
        await using var context = CreateContext();
        var resource = new ResourceEntity
        {
            Name = $"resource_{Guid.NewGuid():N}",
            Type = ResourceType.Equipment,
            Capacity = capacity,
            IsActive = true
        };
        context.Resources.Add(resource);
        await context.SaveChangesAsync();
        return resource;
    }

    private async Task<BookingEntity> SeedBookingAsync(int resourceId, int userId, DateTime start, DateTime end)
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
        return booking;
    }

    private async Task<WaitlistEntryEntity> SeedWaitlistEntryAsync(
        int resourceId, int userId, DateTime start, DateTime end, DateTime createdAt)
    {
        await using var context = CreateContext();
        var entry = new WaitlistEntryEntity
        {
            ResourceId = resourceId,
            UserId = userId,
            RequestedStartTime = start,
            RequestedEndTime = end,
            CreatedAt = createdAt,
            Status = WaitlistStatus.Waiting
        };
        context.WaitlistEntries.Add(entry);
        await context.SaveChangesAsync();
        return entry;
    }

    // Holds every task at a starting line, then releases them together. RunContinuationsAsynchronously
    // matters: without it, SetResult would run the waiting continuations one after another on this
    // thread instead of dispatching them to the thread pool in parallel.
    private static TaskCompletionSource NewStartingGate() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task CreateAsync_ManyConcurrentRequestsForTheSameSlot_NeverExceedsCapacity()
    {
        // The automated version of the NBomber run: capacity 3, many simultaneous requests for the
        // same window. Exactly 3 may win, everyone else must be waitlisted.
        const int capacity = 3;
        const int requests = 20;

        var resource = await SeedResourceAsync(capacity);
        var users = new List<UserEntity>();
        for (var i = 0; i < requests; i++) users.Add(await SeedUserAsync());

        var dto = new CreateBookingDto(At(14), At(15), resource.Id);
        var gate = NewStartingGate();

        var tasks = users.Select(async user =>
        {
            await gate.Task;
            await using var context = CreateContext();
            var (outcome, _) = await new BookingService(context).CreateAsync(dto, user.Id, CancellationToken.None);
            return outcome;
        }).ToList();

        gate.SetResult();
        var outcomes = await Task.WhenAll(tasks);

        Assert.Equal(capacity, outcomes.Count(o => o == BookingCreationResult.Success));
        Assert.Equal(requests - capacity, outcomes.Count(o => o == BookingCreationResult.WaitListed));

        // Check the database, not just the return values.
        await using var verify = CreateContext();
        Assert.Equal(capacity, await verify.Bookings
            .CountAsync(b => b.ResourceId == resource.Id && b.Status == BookingStatus.Confirmed));
        Assert.Equal(requests - capacity, await verify.WaitlistEntries
            .CountAsync(w => w.ResourceId == resource.Id && w.Status == WaitlistStatus.Waiting));
    }

    [Fact]
    public async Task CancelAsync_TwoConcurrentCancellations_PromoteTheSameWaiterOnlyOnce()
    {
        // Capacity 2, both slots taken, one person waiting. Both bookings are cancelled at the same
        // moment. Each cancellation runs its own promotion pass; without the resource lock, both
        // passes can read the entry as Waiting and each create a booking for the same person.
        var firstOwner = await SeedUserAsync();
        var secondOwner = await SeedUserAsync();
        var alice = await SeedUserAsync();
        var resource = await SeedResourceAsync(capacity: 2);
        var firstBooking = await SeedBookingAsync(resource.Id, firstOwner.Id, At(14), At(15));
        var secondBooking = await SeedBookingAsync(resource.Id, secondOwner.Id, At(14), At(15));
        var entry = await SeedWaitlistEntryAsync(resource.Id, alice.Id, At(14), At(15),
            createdAt: DateTime.UtcNow.AddMinutes(-5));

        var gate = NewStartingGate();

        var tasks = new[] { firstBooking.Id, secondBooking.Id }.Select(async bookingId =>
        {
            await gate.Task;
            await using var context = CreateContext();
            // Admin, so the 2-hour window doesn't apply; the user id is irrelevant for admins.
            return await new BookingService(context).CancelAsync(bookingId, 0, true, CancellationToken.None);
        }).ToList();

        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(BookingCancellationResult.Success, r));

        await using var verify = CreateContext();
        var bookings = await verify.Bookings.Where(b => b.ResourceId == resource.Id).ToListAsync();

        Assert.Equal(2, bookings.Count(b => b.Status == BookingStatus.Cancelled));

        // Single() throws if Alice ended up with two bookings, which is exactly the bug being guarded against.
        var alices = Assert.Single(bookings, b => b.UserId == alice.Id);
        Assert.Equal(BookingStatus.Confirmed, alices.Status);

        Assert.Equal(WaitlistStatus.Promoted,
            (await verify.WaitlistEntries.SingleAsync(w => w.Id == entry.Id)).Status);
    }
}