using BookingApi.Data;
using BookingApi.Dto.AuditLog;
using BookingApi.Jobs;
using BookingApi.Service;
using BookingApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace BookingApi.Tests.Service;

[Collection("Postgres")]
public class WaitlistExpirationTests(PostgresFixture fixture)
{
    // The sweep expires EVERY Waiting entry with a start time <= now, across the whole shared database.
    // Isolation rules that keep these tests from touching (or being touched by) any other test class:
    //   * "now" is a distinct date far in the past (counting downward), so no other class's data -- all of it
    //     dated in the real present or future -- is ever <= it;
    //   * stale rows are swept to Expired by the test itself, so no Waiting row is left behind in the past
    //     for another class's sweep to pick up and miscount;
    //   * "upcoming" rows are dated far in the real future, so they stay Waiting but can never be reached
    //     by any sweep.
    private static int _eraCounter;

    private static DateTime NewNow() =>
        new(1900 - 2 * Interlocked.Increment(ref _eraCounter), 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static DateTime FarFuture() => DateTime.UtcNow.AddYears(10);

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options);
    }

    private async Task<List<int>> SweepAsync(DateTime now)
    {
        await using var context = CreateContext();
        return await WaitlistExpirationService.ExpireStaleEntriesAsync(
            context, new AuditLogService(context), now, CancellationToken.None);
    }

    // ---------- seeding ----------

    private async Task<int> SeedUserAsync()
    {
        await using var context = CreateContext();
        var user = new UserEntity
        {
            Username = $"u_{Guid.NewGuid():N}",
            PasswordHash = "not-a-real-hash",
            Role = UserRole.User,
            IsActive = true
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task<int> SeedResourceAsync()
    {
        await using var context = CreateContext();
        var resource = new ResourceEntity
        {
            Name = "Expiry test resource",
            Type = ResourceType.Room,
            Capacity = 1,
            IsActive = true
        };
        context.Resources.Add(resource);
        await context.SaveChangesAsync();
        return resource.Id;
    }

    private async Task<int> SeedEntryAsync(int resourceId, int userId, DateTime start,
        WaitlistStatus status = WaitlistStatus.Waiting)
    {
        await using var context = CreateContext();
        var entry = new WaitlistEntryEntity
        {
            ResourceId = resourceId,
            UserId = userId,
            RequestedStartTime = start,
            RequestedEndTime = start.AddHours(1),
            Status = status,
            CreatedAt = DateTime.UtcNow
        };
        context.WaitlistEntries.Add(entry);
        await context.SaveChangesAsync();
        return entry.Id;
    }

    // ---------- reading ----------

    private async Task<WaitlistStatus> StatusAsync(int entryId)
    {
        await using var context = CreateContext();
        return await context.WaitlistEntries.AsNoTracking()
            .Where(w => w.Id == entryId)
            .Select(w => w.Status)
            .SingleAsync();
    }

    // EntityId is shared across entity types, so entries are matched on the action as well as the ids.
    private async Task<List<AuditLogEntryEntity>> ExpiredEntriesAsync(IReadOnlyCollection<int> entryIds)
    {
        await using var context = CreateContext();
        return await context.AuditLogEntries.AsNoTracking()
            .Where(e => e.Action == ActionType.WaitlistEntryExpired && entryIds.Contains(e.EntityId))
            .ToListAsync();
    }

    // ======================= tests =======================

    [Fact]
    public async Task Sweep_ExpiresStaleWaitingEntries_AndLogsOneSystemEntryPerExpiredRow()
    {
        var now = NewNow();
        var userId = await SeedUserAsync();
        var firstResource = await SeedResourceAsync();
        var secondResource = await SeedResourceAsync();

        var older = await SeedEntryAsync(firstResource, userId, now.AddDays(-3));
        var recent = await SeedEntryAsync(firstResource, userId, now.AddDays(-1));
        var onTheBoundary = await SeedEntryAsync(secondResource, userId, now);   // start == now counts as stale
        var expected = new[] { older, recent, onTheBoundary };

        var swept = await SweepAsync(now);

        Assert.Equal(expected.OrderBy(i => i), swept.OrderBy(i => i));
        foreach (var id in expected) Assert.Equal(WaitlistStatus.Expired, await StatusAsync(id));

        var entries = await ExpiredEntriesAsync(expected);
        Assert.Equal(3, entries.Count);
        Assert.Equal(expected.OrderBy(i => i), entries.Select(e => e.EntityId).OrderBy(i => i));
        Assert.All(entries, e => Assert.Null(e.UserId));   // null user means "the system did this"
    }

    [Fact]
    public async Task Sweep_LeavesUpcomingEntriesWaiting_AndLogsNothingForThem()
    {
        var now = NewNow();
        var userId = await SeedUserAsync();
        var resourceId = await SeedResourceAsync();
        var stale = await SeedEntryAsync(resourceId, userId, now.AddHours(-2));
        var upcoming = await SeedEntryAsync(resourceId, userId, FarFuture());

        var swept = await SweepAsync(now);

        Assert.Equal(stale, Assert.Single(swept));
        Assert.Equal(WaitlistStatus.Expired, await StatusAsync(stale));
        Assert.Equal(WaitlistStatus.Waiting, await StatusAsync(upcoming));
        Assert.Empty(await ExpiredEntriesAsync([upcoming]));
    }

    [Fact]
    public async Task Sweep_IgnoresEntriesThatAreNotWaiting_EvenWhenTheirStartTimeHasPassed()
    {
        var now = NewNow();
        var userId = await SeedUserAsync();
        var resourceId = await SeedResourceAsync();
        var promoted = await SeedEntryAsync(resourceId, userId, now.AddDays(-2), WaitlistStatus.Promoted);
        var withdrawn = await SeedEntryAsync(resourceId, userId, now.AddDays(-2), WaitlistStatus.Withdrawn);
        var alreadyExpired = await SeedEntryAsync(resourceId, userId, now.AddDays(-2), WaitlistStatus.Expired);

        var swept = await SweepAsync(now);

        Assert.Empty(swept);
        Assert.Equal(WaitlistStatus.Promoted, await StatusAsync(promoted));
        Assert.Equal(WaitlistStatus.Withdrawn, await StatusAsync(withdrawn));
        Assert.Equal(WaitlistStatus.Expired, await StatusAsync(alreadyExpired));
        Assert.Empty(await ExpiredEntriesAsync([promoted, withdrawn, alreadyExpired]));
    }

    [Fact]
    public async Task Sweep_RunTwice_SecondRunExpiresNothingAndLogsNothingNew()
    {
        var now = NewNow();
        var userId = await SeedUserAsync();
        var resourceId = await SeedResourceAsync();
        var entryId = await SeedEntryAsync(resourceId, userId, now.AddHours(-1));

        var first = await SweepAsync(now);
        var second = await SweepAsync(now);

        Assert.Equal(entryId, Assert.Single(first));
        Assert.Empty(second);
        Assert.Single(await ExpiredEntriesAsync([entryId]));   // exactly one entry, not one per sweep
    }

    [Fact]
    public async Task Sweep_WhenWritingTheAuditEntryFails_RollsBackTheStatusChange()
    {
        // The atomicity guarantee: the status update and its audit entries commit together or not at all.
        var now = NewNow();
        var userId = await SeedUserAsync();
        var resourceId = await SeedResourceAsync();
        var entryId = await SeedEntryAsync(resourceId, userId, now.AddHours(-1));

        var failingAudit = new Mock<IAuditLogService>();
        failingAudit
            .Setup(s => s.CreateLog(It.IsAny<CreateAuditLogDto>()))
            .Throws(new InvalidOperationException("audit failed"));

        try
        {
            await using (var context = CreateContext())
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    WaitlistExpirationService.ExpireStaleEntriesAsync(
                        context, failingAudit.Object, now, CancellationToken.None));
            }

            // The UPDATE ran inside the transaction, so it must have been rolled back with it.
            Assert.Equal(WaitlistStatus.Waiting, await StatusAsync(entryId));
            Assert.Empty(await ExpiredEntriesAsync([entryId]));
        }
        finally
        {
            // This test deliberately leaves a stale Waiting row; retire it so no other class's sweep ever picks it up.
            await using var cleanup = CreateContext();
            await cleanup.WaitlistEntries
                .Where(w => w.Id == entryId)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WaitlistStatus.Withdrawn));
        }
    }
}