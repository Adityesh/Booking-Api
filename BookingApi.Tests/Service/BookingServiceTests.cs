using System.Globalization;
using BookingApi.Data;
using BookingApi.Dto.Booking;
using BookingApi.Service;
using BookingApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Tests.Service;

[Collection("Postgres")]
public class BookingServiceTests(PostgresFixture fixture)
{

    // xUnit builds a fresh instance of this class for every test, so "the day" is fixed for the
    // duration of one test (no midnight rollover surprises). It's always far in the future, which
    // keeps the 2-hour cancellation rule and any "must start in the future" rule out of the way.
    // Whole hours only: Postgres keeps microseconds, .NET keeps 100ns ticks, so sub-second values
    // would silently lose precision on the round trip and break equality checks.
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

    [Fact]
    public async Task CreateAsync_NonOverlappingTimes_Succeeds()
    {
        // seed a Resource with Capacity = 1
        // seed 1 existing Booking for, say, 9am-10am
        // call CreateAsync for a DIFFERENT window, e.g. 2pm-3pm, same resource
        // assert result.Result == BookingCreationResult.Success
        // this proves it's the OVERLAP that's checked, not just a raw booking count
        var context = CreateContext();
        var startTime = DateTime.UtcNow;
        var endTime = DateTime.UtcNow.AddHours(1);

        var user = new UserEntity()
        {
            IsActive = true,
            Role = UserRole.Admin,
            Username = "Testusername" + DateTime.UtcNow.ToString(CultureInfo.InvariantCulture)
        };

        var resource = new ResourceEntity()
        {
            Capacity = 1,
            IsActive = true,
            Name = "Test Resource",
            Type = ResourceType.Equipment
        };


        var booking = new BookingEntity()
        {
            StartTime = startTime,
            EndTime = endTime,
            Resource = resource,
            Status = BookingStatus.Confirmed,
            User = user
        };

        context.Users.Add(user);
        context.Resources.Add(resource);
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        var bookingService = new BookingService(context);
        var createDto = new CreateBookingDto(startTime.AddHours(2), endTime.AddHours(2), resource.Id);
        var result = await bookingService.CreateAsync(createDto, booking.UserId, CancellationToken.None);

        Assert.Equal(BookingCreationResult.Success, result.result);
        Assert.NotNull(result.booking);
    }

    [Fact]
    public async Task CancelAsync_WithinTwoHours_NonAdmin_ReturnsTooCloseToStartTime()
    {
        // seed a Booking with StartTime ~1 hour from now
        // call CancelAsync with isAdmin: false
        // assert BookingCancellationResult.TooCloseToStartTime
        var context = CreateContext();
        var startTime = DateTime.UtcNow.AddHours(1);
        var endTime = DateTime.UtcNow.AddHours(2);

        var user = new UserEntity()
        {
            IsActive = true,
            Role = UserRole.Admin,
            Username = "Testusername"
        };

        var resource = new ResourceEntity()
        {
            Capacity = 1,
            IsActive = true,
            Name = "Test Resource",
            Type = ResourceType.Equipment
        };


        var booking = new BookingEntity()
        {
            StartTime = startTime,
            EndTime = endTime,
            Resource = resource,
            Status = BookingStatus.Confirmed,
            User = user,
        };

        context.Users.Add(user);
        context.Resources.Add(resource);
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        var bookingService = new BookingService(context);
        var result = await bookingService.CancelAsync(booking.Id, booking.UserId, false, CancellationToken.None);

        Assert.Equal(BookingCancellationResult.TooCloseToStartTime, result);
    }

    [Fact]
    public async Task CancelAsync_WithinTwoHours_Admin_ReturnsSuccess()
    {
        // same seed, isAdmin: true this time
        // assert BookingCancellationResult.Success
        // bonus: reload the booking from context and assert Status == Cancelled
        var context = CreateContext();
        var startTime = DateTime.UtcNow.AddHours(1);
        var endTime = DateTime.UtcNow.AddHours(2);

        var user = new UserEntity()
        {
            IsActive = true,
            Role = UserRole.Admin,
            Username = "Testusername" + DateTime.UtcNow.ToString(CultureInfo.InvariantCulture)
        };

        var resource = new ResourceEntity()
        {
            Capacity = 2,
            IsActive = true,
            Name = "Test Resource",
            Type = ResourceType.Equipment
        };


        var booking = new BookingEntity()
        {
            StartTime = startTime,
            EndTime = endTime,
            Resource = resource,
            Status = BookingStatus.Confirmed,
            User = user
        };

        context.Users.Add(user);
        context.Resources.Add(resource);
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        var bookingService = new BookingService(context);
        var result = await bookingService.CancelAsync(booking.Id, booking.UserId, true, CancellationToken.None);

        Assert.Equal(BookingCancellationResult.Success, result);
    }

    // ---------- seeding helpers (each uses its own context, so nothing stays tracked) ----------
    // The container is shared, so every test creates its own users/resource and only ever looks at
    // rows belonging to its own resource.

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

    private async Task<ResourceEntity> SeedResourceAsync(int capacity, bool isActive = true)
    {
        await using var context = CreateContext();
        var resource = new ResourceEntity
        {
            Name = $"resource_{Guid.NewGuid():N}",
            Type = ResourceType.Equipment,
            Capacity = capacity,
            IsActive = isActive
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
        int resourceId, int userId, DateTime start, DateTime end, DateTime createdAt,
        WaitlistStatus status = WaitlistStatus.Waiting)
    {
        await using var context = CreateContext();
        var entry = new WaitlistEntryEntity
        {
            ResourceId = resourceId,
            UserId = userId,
            RequestedStartTime = start,
            RequestedEndTime = end,
            CreatedAt = createdAt,
            Status = status
        };
        context.WaitlistEntries.Add(entry);
        await context.SaveChangesAsync();
        return entry;
    }

    // ---------- action + verification helpers ----------

    // Admin, so the 2-hour cancellation window never interferes with what's being tested.
    private async Task<BookingCancellationResult> CancelAsAdminAsync(int bookingId)
    {
        await using var context = CreateContext();
        return await new BookingService(context).CancelAsync(bookingId, 0, true, CancellationToken.None);
    }

    // Always read results through a fresh context: the service's own context still tracks the
    // entities it touched, so a tracked read can show state the database doesn't actually have.
    private async Task<WaitlistEntryEntity> GetEntryAsync(int id)
    {
        await using var context = CreateContext();
        return await context.WaitlistEntries.AsNoTracking().SingleAsync(w => w.Id == id);
    }

    private async Task<List<BookingEntity>> GetBookingsAsync(int resourceId)
    {
        await using var context = CreateContext();
        return await context.Bookings.AsNoTracking().Where(b => b.ResourceId == resourceId).ToListAsync();
    }

    private async Task<List<WaitlistEntryEntity>> GetEntriesAsync(int resourceId)
    {
        await using var context = CreateContext();
        return await context.WaitlistEntries.AsNoTracking().Where(w => w.ResourceId == resourceId).ToListAsync();
    }

    // =====================================================================================
    // Enrollment (CreateAsync on a full resource)
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_ResourceFull_ReturnsWaitListed_AndPersistsOneWaitingEntry()
    {
        var owner = await SeedUserAsync();
        var waiter = await SeedUserAsync();
        var resource = await SeedResourceAsync(capacity: 1);
        await SeedBookingAsync(resource.Id, owner.Id, At(14), At(15));

        await using var context = CreateContext();
        var (outcome, booking) = await new BookingService(context)
            .CreateAsync(new CreateBookingDto(At(14), At(15), resource.Id), waiter.Id, CancellationToken.None);

        Assert.Equal(BookingCreationResult.WaitListed, outcome);
        Assert.Null(booking);

        // The entry has to be committed, not just saved inside a transaction that later rolls back.
        var entry = Assert.Single(await GetEntriesAsync(resource.Id));
        Assert.Equal(waiter.Id, entry.UserId);
        Assert.Equal(WaitlistStatus.Waiting, entry.Status);
        Assert.Equal(At(14), entry.RequestedStartTime);
        Assert.Equal(At(15), entry.RequestedEndTime);

        // Waitlisting must not create a booking.
        Assert.Single(await GetBookingsAsync(resource.Id));
    }

    [Fact]
    public async Task CreateAsync_SameUserSameWindowTwice_ReturnsAlreadyWaitListed_AndKeepsOneEntry()
    {
        var owner = await SeedUserAsync();
        var waiter = await SeedUserAsync();
        var resource = await SeedResourceAsync(capacity: 1);
        await SeedBookingAsync(resource.Id, owner.Id, At(14), At(15));
        var dto = new CreateBookingDto(At(14), At(15), resource.Id);

        // Separate contexts, like two separate requests.
        await using (var first = CreateContext())
        {
            var (outcome, _) = await new BookingService(first).CreateAsync(dto, waiter.Id, CancellationToken.None);
            Assert.Equal(BookingCreationResult.WaitListed, outcome);
        }

        await using (var second = CreateContext())
        {
            var (outcome, booking) = await new BookingService(second).CreateAsync(dto, waiter.Id, CancellationToken.None);
            Assert.Equal(BookingCreationResult.AlreadyWaitListed, outcome);
            Assert.Null(booking);
        }

        Assert.Single(await GetEntriesAsync(resource.Id));
    }

    [Fact]
    public async Task CreateAsync_PreviousEntryWasPromoted_AllowsNewWaitlistEntry()
    {
        // Pins down the duplicate check: it must only look at entries that are still Waiting.
        // Scenario: the user was promoted earlier, later lost the booking, and now wants the same window again.
        var owner = await SeedUserAsync();
        var waiter = await SeedUserAsync();
        var resource = await SeedResourceAsync(capacity: 1);
        await SeedBookingAsync(resource.Id, owner.Id, At(14), At(15));
        await SeedWaitlistEntryAsync(resource.Id, waiter.Id, At(14), At(15),
            createdAt: DateTime.UtcNow.AddDays(-1), status: WaitlistStatus.Promoted);

        await using var context = CreateContext();
        var (outcome, _) = await new BookingService(context)
            .CreateAsync(new CreateBookingDto(At(14), At(15), resource.Id), waiter.Id, CancellationToken.None);

        Assert.Equal(BookingCreationResult.WaitListed, outcome);

        var entries = await GetEntriesAsync(resource.Id);
        Assert.Equal(2, entries.Count);
        Assert.Single(entries, e => e.Status == WaitlistStatus.Waiting);
        Assert.Single(entries, e => e.Status == WaitlistStatus.Promoted);
    }

    [Fact]
    public async Task CreateAsync_RoomAvailable_CreatesBooking_AndNoWaitlistEntry()
    {
        var owner = await SeedUserAsync();
        var booker = await SeedUserAsync();
        var resource = await SeedResourceAsync(capacity: 2);
        await SeedBookingAsync(resource.Id, owner.Id, At(14), At(15));

        await using var context = CreateContext();
        var (outcome, booking) = await new BookingService(context)
            .CreateAsync(new CreateBookingDto(At(14), At(15), resource.Id), booker.Id, CancellationToken.None);

        Assert.Equal(BookingCreationResult.Success, outcome);
        Assert.NotNull(booking);
        Assert.Equal(BookingStatus.Confirmed, booking!.Status);
        Assert.Equal(booker.Id, booking.UserId);

        Assert.Equal(2, (await GetBookingsAsync(resource.Id)).Count(b => b.Status == BookingStatus.Confirmed));
        Assert.Empty(await GetEntriesAsync(resource.Id));
    }

    // =====================================================================================
    // Promotion (triggered by CancelAsync)
    // =====================================================================================

    [Fact]
    public async Task CancelAsync_PromotesEarliestWaitingEntry_AndLeavesLaterOneWaiting()
    {
        var owner = await SeedUserAsync();
        var alice = await SeedUserAsync();
        var bob = await SeedUserAsync();
        var resource = await SeedResourceAsync(capacity: 1);
        var booking = await SeedBookingAsync(resource.Id, owner.Id, At(14), At(15));

        // Bob is inserted first on purpose: promotion order has to come from CreatedAt,
        // not from insertion order or Id.
        var bobEntry = await SeedWaitlistEntryAsync(resource.Id, bob.Id, At(14), At(15),
            createdAt: DateTime.UtcNow.AddMinutes(-5));
        var aliceEntry = await SeedWaitlistEntryAsync(resource.Id, alice.Id, At(14), At(15),
            createdAt: DateTime.UtcNow.AddMinutes(-10));

        var result = await CancelAsAdminAsync(booking.Id);

        Assert.Equal(BookingCancellationResult.Success, result);
        Assert.Equal(WaitlistStatus.Promoted, (await GetEntryAsync(aliceEntry.Id)).Status);
        Assert.Equal(WaitlistStatus.Waiting, (await GetEntryAsync(bobEntry.Id)).Status);

        var bookings = await GetBookingsAsync(resource.Id);
        Assert.Equal(BookingStatus.Cancelled, bookings.Single(b => b.Id == booking.Id).Status);

        var promoted = Assert.Single(bookings, b => b.Status == BookingStatus.Confirmed);
        Assert.Equal(alice.Id, promoted.UserId);
        Assert.Equal(At(14), promoted.StartTime);
        Assert.Equal(At(15), promoted.EndTime);
    }

    [Fact]
    public async Task CancelAsync_SkipsEntryWhoseWindowIsStillFull_AndPromotesNextEligible()
    {
        // Capacity 1. X (14-15) gets cancelled, Y (16-17) stays.
        // Alice is first in line but wants 16-17, which is still full -> must be skipped, not blocking the queue.
        // Bob is second in line and wants 14-15, which just opened up -> promoted.
        var userX = await SeedUserAsync();
        var userY = await SeedUserAsync();
        var alice = await SeedUserAsync();
        var bob = await SeedUserAsync();
        var resource = await SeedResourceAsync(capacity: 1);
        var bookingX = await SeedBookingAsync(resource.Id, userX.Id, At(14), At(15));
        var bookingY = await SeedBookingAsync(resource.Id, userY.Id, At(16), At(17));

        var aliceEntry = await SeedWaitlistEntryAsync(resource.Id, alice.Id, At(16), At(17),
            createdAt: DateTime.UtcNow.AddMinutes(-10));
        var bobEntry = await SeedWaitlistEntryAsync(resource.Id, bob.Id, At(14), At(15),
            createdAt: DateTime.UtcNow.AddMinutes(-5));

        var result = await CancelAsAdminAsync(bookingX.Id);

        Assert.Equal(BookingCancellationResult.Success, result);
        Assert.Equal(WaitlistStatus.Waiting, (await GetEntryAsync(aliceEntry.Id)).Status);
        Assert.Equal(WaitlistStatus.Promoted, (await GetEntryAsync(bobEntry.Id)).Status);

        var bookings = await GetBookingsAsync(resource.Id);
        Assert.Equal(BookingStatus.Confirmed, bookings.Single(b => b.Id == bookingY.Id).Status);

        var bobsBooking = Assert.Single(bookings, b => b.UserId == bob.Id);
        Assert.Equal(BookingStatus.Confirmed, bobsBooking.Status);
        Assert.Equal(At(14), bobsBooking.StartTime);
        Assert.Equal(At(15), bobsBooking.EndTime);

        Assert.DoesNotContain(bookings, b => b.UserId == alice.Id);
    }

    [Fact]
    public async Task CancelAsync_PromotesMultipleEntries_WhenOneCancellationFreesEnoughRoom()
    {
        // Capacity 1. One 2-hour booking is cancelled; two waiters want one hour each, back to back.
        // Both fit. The second one only gets promoted correctly if the first promotion was saved
        // before the second overlap check ran.
        var owner = await SeedUserAsync();
        var alice = await SeedUserAsync();
        var bob = await SeedUserAsync();
        var resource = await SeedResourceAsync(capacity: 1);
        var booking = await SeedBookingAsync(resource.Id, owner.Id, At(14), At(16));

        var aliceEntry = await SeedWaitlistEntryAsync(resource.Id, alice.Id, At(14), At(15),
            createdAt: DateTime.UtcNow.AddMinutes(-10));
        var bobEntry = await SeedWaitlistEntryAsync(resource.Id, bob.Id, At(15), At(16),
            createdAt: DateTime.UtcNow.AddMinutes(-5));

        var result = await CancelAsAdminAsync(booking.Id);

        Assert.Equal(BookingCancellationResult.Success, result);
        Assert.Equal(WaitlistStatus.Promoted, (await GetEntryAsync(aliceEntry.Id)).Status);
        Assert.Equal(WaitlistStatus.Promoted, (await GetEntryAsync(bobEntry.Id)).Status);

        var confirmed = (await GetBookingsAsync(resource.Id)).Where(b => b.Status == BookingStatus.Confirmed).ToList();
        Assert.Equal(2, confirmed.Count);

        var alices = Assert.Single(confirmed, b => b.UserId == alice.Id);
        Assert.Equal(At(14), alices.StartTime);
        Assert.Equal(At(15), alices.EndTime);

        var bobs = Assert.Single(confirmed, b => b.UserId == bob.Id);
        Assert.Equal(At(15), bobs.StartTime);
        Assert.Equal(At(16), bobs.EndTime);
    }

    [Fact]
    public async Task CancelAsync_IgnoresEntriesWhoseStartTimeHasPassed()
    {
        // The expiration job only runs every ~20 minutes, so an entry can still be "Waiting"
        // after its window started. Promotion must not turn it into a booking in the past.
        // The window has no competing bookings at all, so without the start-time filter it WOULD be promoted.
        var owner = await SeedUserAsync();
        var alice = await SeedUserAsync();
        var resource = await SeedResourceAsync(capacity: 1);
        var booking = await SeedBookingAsync(resource.Id, owner.Id, At(14), At(15));

        var staleEntry = await SeedWaitlistEntryAsync(resource.Id, alice.Id,
            DateTime.UtcNow.AddHours(-3), DateTime.UtcNow.AddHours(-2),
            createdAt: DateTime.UtcNow.AddHours(-5));

        var result = await CancelAsAdminAsync(booking.Id);

        Assert.Equal(BookingCancellationResult.Success, result);
        Assert.Equal(WaitlistStatus.Waiting, (await GetEntryAsync(staleEntry.Id)).Status);
        Assert.DoesNotContain(await GetBookingsAsync(resource.Id), b => b.UserId == alice.Id);
    }

    [Fact]
    public async Task CancelAsync_InactiveResource_StillCancels_ButPromotesNobody()
    {
        var owner = await SeedUserAsync();
        var alice = await SeedUserAsync();
        var resource = await SeedResourceAsync(capacity: 1, isActive: false);
        var booking = await SeedBookingAsync(resource.Id, owner.Id, At(14), At(15));
        var aliceEntry = await SeedWaitlistEntryAsync(resource.Id, alice.Id, At(14), At(15),
            createdAt: DateTime.UtcNow.AddMinutes(-10));

        var result = await CancelAsAdminAsync(booking.Id);

        Assert.Equal(BookingCancellationResult.Success, result);
        Assert.Equal(WaitlistStatus.Waiting, (await GetEntryAsync(aliceEntry.Id)).Status);
        Assert.DoesNotContain(await GetBookingsAsync(resource.Id), b => b.UserId == alice.Id);
    }
}