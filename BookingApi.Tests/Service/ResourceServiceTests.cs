using BookingApi.Data;
using BookingApi.Service;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Tests.Service;

public class ResourceServiceTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task GetByIdAsync_InactiveResourceNonAdmin_ReturnsNull()
    {
        // Arrange: seed one resource with IsActive = false
        // Act: call GetByIdAsync(id, isAdmin: false)
        // Assert: result is null
        var context = CreateContext();
        context.Resources.Add(new ResourceEntity
        {
            Id = 1, IsActive = false, Name = "Test Resource 1", Capacity = 5, Type = ResourceType.Equipment
        });
        await context.SaveChangesAsync();

        var resourceService = new ResourceService(context);
        var result = await resourceService.GetByIdAsync(1, false, CancellationToken.None);

        Assert.Null(result);

    }

    [Fact]
    public async Task GetByIdAsync_InactiveResourceAdmin_ReturnsResource()
    {
        // same seed, isAdmin: true this time — assert it comes back, not null
        var context = CreateContext();
        var resource = new ResourceEntity
        {
            Id = 1, IsActive = false, Name = "Test Resource 1", Capacity = 5, Type = ResourceType.Equipment
        };
        context.Resources.Add(resource);
        await context.SaveChangesAsync();

        var resourceService = new ResourceService(context);
        var result = await resourceService.GetByIdAsync(1, true, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(result.Name, resource.Name);
    }

    [Fact]
    public async Task GetAllAsync_IncludeInactiveFalse_ExcludesInactiveResources()
    {
        // seed ONE active + ONE inactive resource
        // call GetAllAsync(includeInActive: false)
        // assert the result has exactly 1 item, and it's the active one
        ResourceEntity[] resources = [
            new()
            {
                Id = 1, Name = "Active Resource", Capacity = 5, IsActive = true
            },
            new()
            {
                Id = 2, Name = "InActive Resource", Capacity = 5, IsActive = false
            }
        ];
        var context = CreateContext();
        context.Resources.AddRange(resources);
        await context.SaveChangesAsync();

        var resourceService = new ResourceService(context);
        var result = await resourceService.GetAllAsync(false, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(result[0].Name, resources[0].Name);
    }

    [Fact]
    public async Task DeleteAsync_ExistingResource_SetsIsActiveFalseNotHardDeleted()
    {
        // seed one active resource, call DeleteAsync
        // assert it still exists in context.Resources afterward
        // assert its IsActive is now false — NOT that it's gone
        var resource = new ResourceEntity
        {
            Id = 1, Name = "Test Resource", Capacity = 5, IsActive = true
        };
        var context = CreateContext();
        context.Resources.Add(resource);
        await context.SaveChangesAsync();

        var resourceService = new ResourceService(context);
        var result = await resourceService.DeleteAsync(1, CancellationToken.None);
        Assert.True(result);

        var afterDelete = await context.Resources.FindAsync(1);
        Assert.NotNull(afterDelete);
        Assert.False(afterDelete!.IsActive);


    }
}