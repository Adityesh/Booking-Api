using BookingApi.Dto.Admin;

namespace BookingApi.Service;

public interface IAdminService
{
    public Task<AdminCreationResult> CreateAdmin(string userName, string password, CancellationToken token);
}