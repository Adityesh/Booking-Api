using BookingApi.Data;
using BookingApi.Dto;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Service;

public class ResourceService(AppDbContext context) : IResourceService
{
    public async Task<List<ResourceResponseDto>> GetAllAsync(bool includeInActive, CancellationToken token = default)
    {
        var baseQuery = context.Resources.AsQueryable();

        if (!includeInActive) baseQuery = baseQuery.Where(r => r.IsActive == true);

        return await baseQuery
            .Select(r => new ResourceResponseDto(r.Id, r.Name, r.Type, r.Capacity, r.IsActive))
            .ToListAsync(token);
    }

    public async Task<ResourceResponseDto?> GetByIdAsync(int id, bool isAdmin, CancellationToken token = default)
    {
        var resource = await context.Resources.FirstOrDefaultAsync(x => x.Id == id, token);

        if (resource == null) return null;
        if (!resource.IsActive && !isAdmin) return null;

        return new ResourceResponseDto(resource.Id, resource.Name, resource.Type, resource.Capacity, resource.IsActive);
    }

    public async Task<ResourceResponseDto> CreateAsync(CreateResourceDto dto, CancellationToken token)
    {
        var newResource = new ResourceEntity()
        {
            Name = dto.Name,
            Type = dto.Type,
            Capacity = dto.Capacity,
            IsActive = true
        };
        context.Resources.Add(newResource);
        await context.SaveChangesAsync(token);

        return new ResourceResponseDto(newResource.Id, newResource.Name, newResource.Type, newResource.Capacity, newResource.IsActive);
    }

    public async Task<bool> UpdateAsync(int id, UpdateResourceDto dto, CancellationToken token)
    {
        var resourceExists = await context.Resources.FirstOrDefaultAsync(r => r.Id == id, token);
        if (resourceExists == null) return false;

        resourceExists.Name = dto.Name;
        resourceExists.Type = dto.Type;
        resourceExists.Capacity = dto.Capacity;
        var updateCount = await context.SaveChangesAsync(token);

        return updateCount > 0;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken token)
    {
        var resource = await context.Resources.FirstOrDefaultAsync(r => r.Id == id, token);
        if (resource == null) return false;

        resource.IsActive = false;
        await context.SaveChangesAsync(token);
        return true;
    }
}