using BookingApi.Data;
using BookingApi.Dto.AuditLog;
using BookingApi.Service;
using BookingApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Tests.Service;

[Collection("Postgres")]
public class AuditLogServiceTests(PostgresFixture fixture)
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options);
    }

    // Audit entries have no FK on EntityId, so a large random id isolates each test from the others
    // (the Postgres container is shared across the whole collection).
    private static int NewEntityId() => Random.Shared.Next(1_000_000, int.MaxValue);

    private async Task<int> SeedUserAsync()
    {
        await using var context = CreateContext();
        var user = new UserEntity
        {
            Username = $"audit_{Guid.NewGuid():N}",
            PasswordHash = "not-a-real-hash",
            Role = UserRole.User,
            IsActive = true
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task<List<AuditLogEntryEntity>> EntriesForAsync(int entityId)
    {
        await using var context = CreateContext();
        return await context.AuditLogEntries.AsNoTracking().Where(e => e.EntityId == entityId).ToListAsync();
    }

    [Fact]
    public async Task CreateLog_DoesNotPersistUntilTheCallerSaves()
    {
        var entityId = NewEntityId();
        await using var context = CreateContext();
        var service = new AuditLogService(context);

        service.CreateLog(new CreateAuditLogDto(entityId, null, ActionType.WaitlistEntryExpired));

        Assert.Empty(await EntriesForAsync(entityId));   // nothing written yet: the service never saves on its own

        await context.SaveChangesAsync();

        Assert.Single(await EntriesForAsync(entityId));
    }

    [Fact]
    public async Task CreateLog_WithUser_PersistsActionEntityUserAndUtcTimestamp()
    {
        var userId = await SeedUserAsync();
        var entityId = NewEntityId();
        var before = DateTime.UtcNow.AddSeconds(-1);

        await using (var context = CreateContext())
        {
            new AuditLogService(context).CreateLog(new CreateAuditLogDto(entityId, userId, ActionType.BookingCreated));
            await context.SaveChangesAsync();
        }

        var after = DateTime.UtcNow.AddSeconds(1);
        var entry = Assert.Single(await EntriesForAsync(entityId));
        Assert.Equal(ActionType.BookingCreated, entry.Action);
        Assert.Equal(userId, entry.UserId);
        Assert.InRange(entry.Timestamp, before, after);
    }

    [Fact]
    public async Task CreateLog_SystemAction_PersistsWithNullUserId()
    {
        var entityId = NewEntityId();

        await using (var context = CreateContext())
        {
            new AuditLogService(context).CreateLog(new CreateAuditLogDto(entityId, null, ActionType.WaitlistEntryExpired));
            await context.SaveChangesAsync();
        }

        var entry = Assert.Single(await EntriesForAsync(entityId));
        Assert.Null(entry.UserId);
        Assert.Equal(ActionType.WaitlistEntryExpired, entry.Action);
    }

    [Fact]
    public async Task CreateLog_UnknownUserId_IsRejectedByTheForeignKey()
    {
        // Guards the FK: a stray or swapped id (e.g. an entity id passed as the user id) must fail loudly, not be stored.
        await using var context = CreateContext();
        new AuditLogService(context).CreateLog(new CreateAuditLogDto(NewEntityId(), 2_000_000_000, ActionType.BookingCancelled));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task CreateLog_WhenTheSurroundingTransactionRollsBack_LeavesNoEntry()
    {
        // The property the whole design rests on: the entry lives or dies with the action it describes.
        var entityId = NewEntityId();

        await using (var context = CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            new AuditLogService(context).CreateLog(new CreateAuditLogDto(entityId, null, ActionType.BookingCancelled));
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        Assert.Empty(await EntriesForAsync(entityId));
    }
}