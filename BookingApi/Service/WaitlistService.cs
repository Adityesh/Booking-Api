using BookingApi.Data;
using BookingApi.Dto.AuditLog;
using BookingApi.Dto.Waitlist;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Service;

public class WaitlistService(AppDbContext context, IAuditLogService auditLogService) : IWaitlistService
{
    public async Task<IList<WaitlistEntryResponseDto>> GetMineAsync(int userId, WaitlistStatus? status, CancellationToken token)
    {
        var baseQuery = context.WaitlistEntries
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .AsQueryable();

        if (status.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.Status == status.Value);
        }

        return await baseQuery
            .Select(x => new WaitlistEntryResponseDto(x.Id, x.ResourceId, x.RequestedStartTime, x.RequestedEndTime, x.Status, x.CreatedAt))
            .ToListAsync(token);
    }

    public async Task<WithdrawWaitlistResult> WithdrawAsync(int id, int userId, CancellationToken token)
    {
        var resourceId = await context.WaitlistEntries
            .AsNoTracking()
            .Where(x => x.Id == id && x.UserId == userId)
            .Select(x => (int?)x.ResourceId)
            .FirstOrDefaultAsync(token);

        if (resourceId is null) return WithdrawWaitlistResult.NotFound;


        await using var transaction = await context.Database.BeginTransactionAsync(token);

        try
        {
            await context.Resources
                .FromSqlInterpolated($"SELECT * FROM resources where id = {resourceId} FOR UPDATE")
                .FirstOrDefaultAsync(token);

            var waitlistEntry = await context.WaitlistEntries
                .FirstAsync(x => x.Id == id, token);

            if (waitlistEntry.Status == WaitlistStatus.Withdrawn)
            {
                await transaction.RollbackAsync(token);
                return WithdrawWaitlistResult.Success;
            }

            if (waitlistEntry.Status != WaitlistStatus.Waiting)
            {
                await transaction.RollbackAsync(token);
                return WithdrawWaitlistResult.NotWaiting;
            }

            waitlistEntry.Status = WaitlistStatus.Withdrawn;
            auditLogService.CreateLog(new CreateAuditLogDto(id, userId, ActionType.WaitlistEntryWithdrawn));
            await context.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return WithdrawWaitlistResult.Success;

        }
        catch (Exception)
        {
            await transaction.RollbackAsync(token);
            throw;
        }
    }
}