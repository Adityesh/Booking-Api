using BookingApi.Dto;

namespace BookingApi.Service;

public interface IResourceService
{
    public Task<List<ResourceResponseDto>> GetAllAsync(bool includeInActive, CancellationToken token = default);

    public Task<ResourceResponseDto?> GetByIdAsync(int id, bool isAdmin,
        CancellationToken token = default);
    public Task<ResourceResponseDto> CreateAsync(CreateResourceDto dto, CancellationToken token);
    public Task<bool> UpdateAsync(int id, UpdateResourceDto dto, CancellationToken token);
    public Task<bool> DeleteAsync(int id, CancellationToken token);

}