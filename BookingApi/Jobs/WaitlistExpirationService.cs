using BookingApi.Data;
using BookingApi.Dto.AuditLog;
using BookingApi.Service;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Jobs;

public class WaitlistExpirationService(IServiceScopeFactory scopeFactory, ILogger<WaitlistExpirationService> logger) : BackgroundService
{
    // The sweep itself, separate from the loop so it can be tested with a fixed "now".
    public static async Task<List<int>> ExpireStaleEntriesAsync(AppDbContext context, IAuditLogService auditLogService, DateTime now, CancellationToken token)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var result = await context.Database
            .SqlQuery<int>(
                $"UPDATE waitlist_entries SET status={nameof(WaitlistStatus.Expired)} WHERE status={nameof(WaitlistStatus.Waiting)} AND requested_start_time <= {now} RETURNING id AS \"Value\"").ToListAsync(token);
        foreach (var id in result)
        {
            auditLogService.CreateLog(new CreateAuditLogDto(id, null, ActionType.WaitlistEntryExpired));
        }

        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);

        return result;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {

            try
            {
                using var scope = scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var auditLogService = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
                var expired = await ExpireStaleEntriesAsync(context, auditLogService, DateTime.UtcNow, stoppingToken);
                if (expired.Count > 0) logger.LogInformation("Expired {Count} waitlist entries", expired.Count);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Waitlist expiration sweep failed.");   // no rethrow: that would stop the host
            }

            await Task.Delay(TimeSpan.FromMinutes(20), stoppingToken);
        }
    }
}