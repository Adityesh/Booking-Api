using BookingApi.Data;
using BookingApi.Dto.Waitlist;

namespace BookingApi.Service;

public interface IWaitlistService
{
    public Task<IList<WaitlistEntryResponseDto>> GetMineAsync(int userId, WaitlistStatus? status, CancellationToken token);
    public Task<WithdrawWaitlistResult> WithdrawAsync(int id, int userId, CancellationToken token);

}