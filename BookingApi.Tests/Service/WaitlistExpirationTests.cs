using BookingApi.Data;
using BookingApi.Jobs;
using BookingApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Tests.Service;

[Collection("Postgres")]
public class WaitlistExpirationTests(PostgresFixture fixture)
{
    private static int _eraCounter;

    // The sweep takes "now" as a parameter, so every test invents its own isolated moment in the distant
    // past. The sweep is global (it touches every Waiting entry that has started) and the container is
    // shared with other test classes, so isolating by era keeps the returned counts exact and stops
    // tests from expiring each other's rows.
    private static DateTime NewIsolatedNow() =>
        new(2000 - 2 * Interlocked.Increment(ref _eraCounter), 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options);
    }

    private async Task<(int UserId, int ResourceId)> SeedUserAndResourceAsync()
    {
        await using var context = CreateContext();
        var user = new UserEntity
        {
            Username = $"user_{Guid.NewGuid():N}",
            PasswordHash = "not-a-real-hash",
            Role = UserRole.User,
            IsActive = true
        };
        var resource = new ResourceEntity
        {
            Name = $"resource_{Guid.NewGuid():N}",
            Type = ResourceType.Equipment,
            Capacity = 1,
            IsActive = true
        };
        context.Users.Add(user);
        context.Resources.Add(resource);
        await context.SaveChangesAsync();
        return (user.Id, resource.Id);
    }

    private async Task<WaitlistEntryEntity> SeedEntryAsync(
        int userId, int resourceId, DateTime start, WaitlistStatus status = WaitlistStatus.Waiting)
    {
        await using var context = CreateContext();
        var entry = new WaitlistEntryEntity
        {
            UserId = userId,
            ResourceId = resourceId,
            RequestedStartTime = start,
            RequestedEndTime = start.AddHours(1),
            CreatedAt = start.AddHours(-2),
            Status = status
        };
        context.WaitlistEntries.Add(entry);
        await context.SaveChangesAsync();
        return entry;
    }

    private async Task<int> SweepAsync(DateTime now)
    {
        await using var context = CreateContext();
        return await WaitlistExpirationService.ExpireStaleEntriesAsync(context, now, CancellationToken.None);
    }

    // Fresh context, so this reads what's actually in the database.
    private async Task<WaitlistStatus> GetStatusAsync(int entryId)
    {
        await using var context = CreateContext();
        return (await context.WaitlistEntries.AsNoTracking().SingleAsync(w => w.Id == entryId)).Status;
    }

    [Fact]
    public async Task ExpireStaleEntriesAsync_ExpiresWaitingEntriesWhoseWindowHasStarted()
    {
        var now = NewIsolatedNow();
        var (userId, resourceId) = await SeedUserAndResourceAsync();
        var alreadyStarted = await SeedEntryAsync(userId, resourceId, now.AddHours(-1));
        var startsExactlyNow = await SeedEntryAsync(userId, resourceId, now);     // boundary: <= now counts as started
        var stillUpcoming = await SeedEntryAsync(userId, resourceId, now.AddHours(1));

        var expired = await SweepAsync(now);

        Assert.Equal(2, expired);
        Assert.Equal(WaitlistStatus.Expired, await GetStatusAsync(alreadyStarted.Id));
        Assert.Equal(WaitlistStatus.Expired, await GetStatusAsync(startsExactlyNow.Id));
        Assert.Equal(WaitlistStatus.Waiting, await GetStatusAsync(stillUpcoming.Id));
    }

    [Fact]
    public async Task ExpireStaleEntriesAsync_LeavesEntriesThatAreNotWaitingAlone()
    {
        // Promoted and Withdrawn entries have long-past start times too, but they must keep their status.
        var now = NewIsolatedNow();
        var (userId, resourceId) = await SeedUserAndResourceAsync();
        var promoted = await SeedEntryAsync(userId, resourceId, now.AddHours(-3), WaitlistStatus.Promoted);
        var withdrawn = await SeedEntryAsync(userId, resourceId, now.AddHours(-2), WaitlistStatus.Withdrawn);

        var expired = await SweepAsync(now);

        Assert.Equal(0, expired);
        Assert.Equal(WaitlistStatus.Promoted, await GetStatusAsync(promoted.Id));
        Assert.Equal(WaitlistStatus.Withdrawn, await GetStatusAsync(withdrawn.Id));
    }

    [Fact]
    public async Task ExpireStaleEntriesAsync_RunTwice_SecondRunChangesNothing()
    {
        var now = NewIsolatedNow();
        var (userId, resourceId) = await SeedUserAndResourceAsync();
        var entry = await SeedEntryAsync(userId, resourceId, now.AddHours(-1));

        var first = await SweepAsync(now);
        var second = await SweepAsync(now);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(WaitlistStatus.Expired, await GetStatusAsync(entry.Id));
    }
}