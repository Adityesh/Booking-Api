using BookingApi.Data;
using BookingApi.Dto;
using BookingApi.Service;
using BookingApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Tests.Service;

[Collection("Postgres")]
public class ResourceServiceTests(PostgresFixture fixture)
{
    private static readonly ActionType[] ResourceActions =
        [ActionType.ResourceCreated, ActionType.ResourceUpdated, ActionType.ResourceDeleted];

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options);
    }

    // The real audit service on the SAME context: this is exactly how DI wires it per request.
    private static ResourceService CreateService(AppDbContext context) =>
        new(context, new AuditLogService(context));

    private async Task<int> SeedAdminAsync()
    {
        await using var context = CreateContext();
        var admin = new UserEntity
        {
            Username = $"admin_{Guid.NewGuid():N}",
            PasswordHash = "not-a-real-hash",
            Role = UserRole.Admin,
            IsActive = true
        };
        context.Users.Add(admin);
        await context.SaveChangesAsync();
        return admin.Id;
    }

    private async Task<int> SeedResourceAsync(bool isActive = true)
    {
        await using var context = CreateContext();
        var resource = new ResourceEntity
        {
            Name = "Seeded resource",
            Type = ResourceType.Room,
            Capacity = 4,
            IsActive = isActive
        };
        context.Resources.Add(resource);
        await context.SaveChangesAsync();
        return resource.Id;
    }

    // EntityId is shared across entity types (a booking and a resource can both have id 5),
    // so always filter by the resource actions too, or entries from other test classes would leak in.
    private async Task<List<AuditLogEntryEntity>> ResourceEntriesAsync(int resourceId)
    {
        await using var context = CreateContext();
        return await context.AuditLogEntries.AsNoTracking()
            .Where(e => e.EntityId == resourceId && ResourceActions.Contains(e.Action))
            .ToListAsync();
    }

    // ---------- Create ----------

    [Fact]
    public async Task CreateAsync_PersistsResource_AndLogsResourceCreatedWithTheNewIdAndActor()
    {
        var adminId = await SeedAdminAsync();
        ResourceResponseDto created;

        await using (var context = CreateContext())
        {
            created = await CreateService(context)
                .CreateAsync(new CreateResourceDto("Board room", ResourceType.Room, 8), adminId, CancellationToken.None);
        }

        var entry = Assert.Single(await ResourceEntriesAsync(created.Id));
        Assert.Equal(ActionType.ResourceCreated, entry.Action);
        Assert.Equal(adminId, entry.UserId);

        await using var verify = CreateContext();
        Assert.True(await verify.Resources.AnyAsync(r => r.Id == created.Id));
    }

    // ---------- Update ----------

    [Fact]
    public async Task UpdateAsync_ChangedFields_UpdatesResource_AndLogsResourceUpdated()
    {
        var adminId = await SeedAdminAsync();
        var resourceId = await SeedResourceAsync();

        bool result;
        await using (var context = CreateContext())
        {
            result = await CreateService(context)
                .UpdateAsync(resourceId, new UpdateResourceDto("Renamed", 9, ResourceType.Equipment), adminId, CancellationToken.None);
        }

        Assert.True(result);
        var entry = Assert.Single(await ResourceEntriesAsync(resourceId));
        Assert.Equal(ActionType.ResourceUpdated, entry.Action);
        Assert.Equal(adminId, entry.UserId);

        await using var verify = CreateContext();
        var resource = await verify.Resources.AsNoTracking().SingleAsync(r => r.Id == resourceId);
        Assert.Equal("Renamed", resource.Name);
        Assert.Equal(ResourceType.Equipment, resource.Type);
        Assert.Equal(9, resource.Capacity);
    }

    [Fact]
    public async Task UpdateAsync_IdenticalValues_ReturnsFalse_AndLogsNothing()
    {
        var adminId = await SeedAdminAsync();
        var resourceId = await SeedResourceAsync();   // seeded as "Seeded resource", Room, capacity 4

        bool result;
        await using (var context = CreateContext())
        {
            result = await CreateService(context)
                .UpdateAsync(resourceId, new UpdateResourceDto("Seeded resource", 4, ResourceType.Room), adminId, CancellationToken.None);
        }

        Assert.False(result);   // the resource exists, so this is not a failure
        Assert.Empty(await ResourceEntriesAsync(resourceId));   // nothing changed, so nothing to audit
    }

    [Fact]
    public async Task UpdateAsync_UnknownResource_ReturnsFalse_AndLogsNothing()
    {
        var adminId = await SeedAdminAsync();

        bool result;
        await using (var context = CreateContext())
        {
            result = await CreateService(context)
                .UpdateAsync(int.MaxValue, new UpdateResourceDto("Ghost", 1, ResourceType.Room), adminId, CancellationToken.None);
        }

        Assert.False(result);
        Assert.Empty(await ResourceEntriesAsync(int.MaxValue));
    }

    // ---------- Delete (soft) ----------

    [Fact]
    public async Task DeleteAsync_ActiveResource_Deactivates_AndLogsResourceDeleted()
    {
        var adminId = await SeedAdminAsync();
        var resourceId = await SeedResourceAsync();

        bool result;
        await using (var context = CreateContext())
        {
            result = await CreateService(context).DeleteAsync(resourceId, adminId, CancellationToken.None);
        }

        Assert.True(result);
        var entry = Assert.Single(await ResourceEntriesAsync(resourceId));
        Assert.Equal(ActionType.ResourceDeleted, entry.Action);
        Assert.Equal(adminId, entry.UserId);

        await using var verify = CreateContext();
        var resource = await verify.Resources.AsNoTracking().SingleAsync(r => r.Id == resourceId);
        Assert.False(resource.IsActive);   // soft delete: the row is still there
    }

    [Fact]
    public async Task DeleteAsync_CalledTwice_IsIdempotent_AndLogsOnlyOnce()
    {
        var adminId = await SeedAdminAsync();
        var resourceId = await SeedResourceAsync();

        bool first, second;
        await using (var context = CreateContext())
            first = await CreateService(context).DeleteAsync(resourceId, adminId, CancellationToken.None);
        await using (var context = CreateContext())
            second = await CreateService(context).DeleteAsync(resourceId, adminId, CancellationToken.None);

        Assert.True(first);
        Assert.True(second);
        Assert.Single(await ResourceEntriesAsync(resourceId));
    }

    [Fact]
    public async Task DeleteAsync_UnknownResource_ReturnsFalse_AndLogsNothing()
    {
        var adminId = await SeedAdminAsync();

        bool result;
        await using (var context = CreateContext())
        {
            result = await CreateService(context).DeleteAsync(int.MaxValue, adminId, CancellationToken.None);
        }

        Assert.False(result);
        Assert.Empty(await ResourceEntriesAsync(int.MaxValue));
    }
}