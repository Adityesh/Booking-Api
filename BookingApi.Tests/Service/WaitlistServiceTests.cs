using BookingApi.Data;
using BookingApi.Dto.Booking;
using BookingApi.Dto.Waitlist;
using BookingApi.Service;
using BookingApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Tests.Service;

[Collection("Postgres")]
public class WaitlistServiceTests(PostgresFixture fixture)
{
    // Fresh class instance per test => the day is stable within a test. Whole hours only, so nothing
    // is lost to Postgres' microsecond precision on the round trip.
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

    // The real audit service on the SAME context, exactly as DI wires it per request.
    private static BookingService NewBookingService(AppDbContext context) =>
        new(context, new AuditLogService(context));

    private static WaitlistService NewWaitlistService(AppDbContext context) =>
        new(context, new AuditLogService(context));

    // ---------- seeding helpers ----------

    private async Task<UserEntity> SeedUserAsync(UserRole role = UserRole.User)
    {
        await using var context = CreateContext();
        var user = new UserEntity
        {
            Username = $"user_{Guid.NewGuid():N}",
            PasswordHash = "not-a-real-hash",
            Role = role,
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

    private async Task<WaitlistEntryEntity> SeedEntryAsync(
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

    // ---------- action + verification helpers (fresh context each time, like separate requests) ----------

    private async Task<WithdrawWaitlistResult> WithdrawAsync(int entryId, int userId)
    {
        await using var context = CreateContext();
        return await NewWaitlistService(context).WithdrawAsync(entryId, userId, CancellationToken.None);
    }

    private async Task<IList<WaitlistEntryResponseDto>> GetMineAsync(int userId, WaitlistStatus? status = null)
    {
        await using var context = CreateContext();
        return await NewWaitlistService(context).GetMineAsync(userId, status, CancellationToken.None);
    }

    private async Task<WaitlistEntryEntity> GetEntryAsync(int id)
    {
        await using var context = CreateContext();
        return await context.WaitlistEntries.AsNoTracking().SingleAsync(w => w.Id == id);
    }

    // EntityId is shared across entity types, so entries are always matched on the action as well as the id.
    private async Task<int> CountEntriesAsync(ActionType action, int entityId)
    {
        await using var context = CreateContext();
        return await context.AuditLogEntries.CountAsync(e => e.Action == action && e.EntityId == entityId);
    }

    // =====================================================================================
    // GetMineAsync
    // =====================================================================================

    [Fact]
    public async Task GetMineAsync_ReturnsOnlyTheCallersEntries()
    {
        var alice = await SeedUserAsync();
        var bob = await SeedUserAsync();
        var resource = await SeedResourceAsync(capacity: 1);
        var aliceEntry = await SeedEntryAsync(resource.Id, alice.Id, At(14), At(15), DateTime.UtcNow.AddMinutes(-10));
        await SeedEntryAsync(resource.Id, bob.Id, At(14), At(15), DateTime.UtcNow.AddMinutes(-5));

        var mine = await GetMineAsync(alice.Id);

        var only = Assert.Single(mine);
        Assert.Equal(aliceEntry.Id, only.Id);
        Assert.Equal(resource.Id, only.ResourceId);
        Assert.Equal(At(14), only.RequestedStartTime);
        Assert.Equal(At(15), only.RequestedEndTime);
        Assert.Equal(WaitlistStatus.Waiting, only.Status);
    }

    [Fact]
    public async Task GetMineAsync_FiltersByStatus_AndReturnsEverythingWhenNoFilterIsGiven()
    {
        var alice = await SeedUserAsync();
        var resource = await SeedResourceAsync(capacity: 1);
        var waiting = await SeedEntryAsync(resource.Id, alice.Id, At(10), At(11), DateTime.UtcNow.AddMinutes(-30));
        await SeedEntryAsync(resource.Id, alice.Id, At(12), At(13), DateTime.UtcNow.AddMinutes(-20),
            WaitlistStatus.Promoted);
        await SeedEntryAsync(resource.Id, alice.Id, At(14), At(15), DateTime.UtcNow.AddMinutes(-10),
            WaitlistStatus.Withdrawn);

        var onlyWaiting = await GetMineAsync(alice.Id, WaitlistStatus.Waiting);
        var everything = await GetMineAsync(alice.Id);

        Assert.Equal(waiting.Id, Assert.Single(onlyWaiting).Id);
        Assert.Equal(3, everything.Count);
    }

    [Fact]
    public async Task GetMineAsync_ReturnsEntriesInTheOrderTheyWereJoined()
    {
        var alice = await SeedUserAsync();
        var resource = await SeedResourceAsync(capacity: 1);

        // Inserted newest-first on purpose, so insertion order can't accidentally satisfy the assertion.
        var newest = await SeedEntryAsync(resource.Id, alice.Id, At(16), At(17), DateTime.UtcNow.AddMinutes(-10));
        var oldest = await SeedEntryAsync(resource.Id, alice.Id, At(12), At(13), DateTime.UtcNow.AddMinutes(-30));
        var middle = await SeedEntryAsync(resource.Id, alice.Id, At(14), At(15), DateTime.UtcNow.AddMinutes(-20));

        var mine = await GetMineAsync(alice.Id);

        Assert.Equal(new[] { oldest.Id, middle.Id, newest.Id }, mine.Select(e => e.Id).ToArray());
    }

    // =====================================================================================
    // WithdrawAsync
    // =====================================================================================

    [Fact]
    public async Task WithdrawAsync_OwnWaitingEntry_MarksItWithdrawn_AndCommits()
    {
        var alice = await SeedUserAsync();
        var resource = await SeedResourceAsync(capacity: 1);
        var entry = await SeedEntryAsync(resource.Id, alice.Id, At(14), At(15), DateTime.UtcNow.AddMinutes(-5));

        var result = await WithdrawAsync(entry.Id, alice.Id);

        Assert.Equal(WithdrawWaitlistResult.Success, result);
        // Read through a fresh context: proves the change was committed, not just saved inside a transaction.
        Assert.Equal(WaitlistStatus.Withdrawn, (await GetEntryAsync(entry.Id)).Status);
    }

    [Fact]
    public async Task WithdrawAsync_SomeoneElsesEntry_ReturnsNotFound_AndLeavesItWaiting()
    {
        var alice = await SeedUserAsync();
        var mallory = await SeedUserAsync();
        var resource = await SeedResourceAsync(capacity: 1);
        var entry = await SeedEntryAsync(resource.Id, alice.Id, At(14), At(15), DateTime.UtcNow.AddMinutes(-5));

        var result = await WithdrawAsync(entry.Id, mallory.Id);

        // NotFound, not "forbidden": the caller can't tell "not yours" from "doesn't exist".
        Assert.Equal(WithdrawWaitlistResult.NotFound, result);
        Assert.Equal(WaitlistStatus.Waiting, (await GetEntryAsync(entry.Id)).Status);
    }

    [Fact]
    public async Task WithdrawAsync_UnknownId_ReturnsNotFound()
    {
        var alice = await SeedUserAsync();

        var result = await WithdrawAsync(int.MaxValue, alice.Id);

        Assert.Equal(WithdrawWaitlistResult.NotFound, result);
    }

    [Fact]
    public async Task WithdrawAsync_CalledTwice_SucceedsBothTimes()
    {
        // Idempotent, like cancelling an already-cancelled booking: a retried request must be safe.
        var alice = await SeedUserAsync();
        var resource = await SeedResourceAsync(capacity: 1);
        var entry = await SeedEntryAsync(resource.Id, alice.Id, At(14), At(15), DateTime.UtcNow.AddMinutes(-5));

        var first = await WithdrawAsync(entry.Id, alice.Id);
        var second = await WithdrawAsync(entry.Id, alice.Id);

        Assert.Equal(WithdrawWaitlistResult.Success, first);
        Assert.Equal(WithdrawWaitlistResult.Success, second);
        Assert.Equal(WaitlistStatus.Withdrawn, (await GetEntryAsync(entry.Id)).Status);
    }

    [Theory]
    [InlineData(WaitlistStatus.Promoted)]
    [InlineData(WaitlistStatus.Expired)]
    public async Task WithdrawAsync_EntryIsNoLongerWaiting_ReturnsNotWaiting_AndLeavesStatusAlone(
        WaitlistStatus currentStatus)
    {
        var alice = await SeedUserAsync();
        var resource = await SeedResourceAsync(capacity: 1);
        var entry = await SeedEntryAsync(resource.Id, alice.Id, At(14), At(15), DateTime.UtcNow.AddMinutes(-5),
            currentStatus);

        var result = await WithdrawAsync(entry.Id, alice.Id);

        Assert.Equal(WithdrawWaitlistResult.NotWaiting, result);
        Assert.Equal(currentStatus, (await GetEntryAsync(entry.Id)).Status);
    }

    [Fact]
    public async Task WithdrawAsync_ThenJoiningAgain_CreatesANewWaitingEntry()
    {
        // The duplicate check in CreateAsync only looks at Waiting entries, so leaving and re-joining must work.
        var owner = await SeedUserAsync();
        var alice = await SeedUserAsync();
        var resource = await SeedResourceAsync(capacity: 1);
        await SeedBookingAsync(resource.Id, owner.Id, At(14), At(15));
        var dto = new CreateBookingDto(At(14), At(15), resource.Id);

        await using (var context = CreateContext())
        {
            var (outcome, _) = await NewBookingService(context).CreateAsync(dto, alice.Id, CancellationToken.None);
            Assert.Equal(BookingCreationResult.WaitListed, outcome);
        }

        var firstEntry = Assert.Single(await GetMineAsync(alice.Id));
        Assert.Equal(WithdrawWaitlistResult.Success, await WithdrawAsync(firstEntry.Id, alice.Id));

        await using (var context = CreateContext())
        {
            var (outcome, _) = await NewBookingService(context).CreateAsync(dto, alice.Id, CancellationToken.None);
            Assert.Equal(BookingCreationResult.WaitListed, outcome);
        }

        var all = await GetMineAsync(alice.Id);
        Assert.Equal(2, all.Count);
        Assert.Single(all, e => e.Status == WaitlistStatus.Withdrawn);
        Assert.Single(all, e => e.Status == WaitlistStatus.Waiting);
    }

    // =====================================================================================
    // Concurrency: withdrawing while a cancellation is promoting the same entry
    // =====================================================================================

    [Fact]
    public async Task WithdrawAsync_RacingAgainstPromotion_NeverLeavesAWithdrawnEntryHoldingABooking()
    {
        // Repeated rounds: which side wins the lock is up to the scheduler, so one round proves little.
        for (var round = 0; round < 10; round++)
        {
            await RunWithdrawVersusPromotionRoundAsync();
        }
    }

    private async Task RunWithdrawVersusPromotionRoundAsync()
    {
        var owner = await SeedUserAsync();
        var alice = await SeedUserAsync();
        var admin = await SeedUserAsync(UserRole.Admin);   // a real user: the cancel is audited with the actor's id
        var resource = await SeedResourceAsync(capacity: 1);
        var booking = await SeedBookingAsync(resource.Id, owner.Id, At(14), At(15));
        var entry = await SeedEntryAsync(resource.Id, alice.Id, At(14), At(15), DateTime.UtcNow.AddMinutes(-5));

        // Starting gate: both operations are released at the same moment. RunContinuationsAsynchronously
        // makes SetResult dispatch them in parallel instead of running them one after another here.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var cancelTask = Task.Run(async () =>
        {
            await gate.Task;
            await using var context = CreateContext();
            // Admin, so the 2-hour window doesn't apply.
            return await NewBookingService(context).CancelAsync(booking.Id, admin.Id, true, CancellationToken.None);
        });

        var withdrawTask = Task.Run(async () =>
        {
            await gate.Task;
            await using var context = CreateContext();
            return await NewWaitlistService(context).WithdrawAsync(entry.Id, alice.Id, CancellationToken.None);
        });

        gate.SetResult();
        var cancelResult = await cancelTask;
        var withdrawResult = await withdrawTask;

        Assert.Equal(BookingCancellationResult.Success, cancelResult);

        var finalEntry = await GetEntryAsync(entry.Id);
        await using var verify = CreateContext();
        var alicesBookings = await verify.Bookings
            .Where(b => b.ResourceId == resource.Id && b.UserId == alice.Id && b.Status == BookingStatus.Confirmed)
            .ToListAsync();

        var withdrawnLogged = await CountEntriesAsync(ActionType.WaitlistEntryWithdrawn, entry.Id);
        var promotedLogged = await CountEntriesAsync(ActionType.WaitlistEntryPromoted, entry.Id);

        // Either side may win; both outcomes are valid as long as the end state is consistent.
        if (withdrawResult == WithdrawWaitlistResult.Success)
        {
            // Withdraw got the lock first: Alice left, so promotion had nobody to promote.
            Assert.Equal(WaitlistStatus.Withdrawn, finalEntry.Status);
            Assert.Empty(alicesBookings);

            // The audit log tells the same story: one withdrawal, no promotion.
            Assert.Equal(1, withdrawnLogged);
            Assert.Equal(0, promotedLogged);
        }
        else
        {
            // Promotion got the lock first: Alice was promoted, so withdrawing is too late.
            Assert.Equal(WithdrawWaitlistResult.NotWaiting, withdrawResult);
            Assert.Equal(WaitlistStatus.Promoted, finalEntry.Status);
            var alicesBooking = Assert.Single(alicesBookings);

            // One promotion, one booking created for it, and no withdrawal: never both, never neither.
            Assert.Equal(1, promotedLogged);
            Assert.Equal(0, withdrawnLogged);
            Assert.Equal(1, await CountEntriesAsync(ActionType.BookingCreated, alicesBooking.Id));
        }
    }
}