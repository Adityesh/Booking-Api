using BookingApi.Data;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Jobs;

public class WaitlistExpirationService(IServiceScopeFactory scopeFactory, ILogger<WaitlistExpirationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var expiredEntries = await context.WaitlistEntries
                    .Where(w => w.RequestedStartTime <= DateTime.UtcNow && w.Status == WaitlistStatus.Waiting)
                    .ToListAsync(stoppingToken);

                foreach (var entry in expiredEntries)
                {
                    entry.Status = WaitlistStatus.Expired;
                }

                if (expiredEntries.Count > 0)
                {
                    await context.SaveChangesAsync(stoppingToken);
                    logger.LogInformation("Expired {Count} waitlist entries", expiredEntries.Count);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Waitlist expiration sweep failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(20), stoppingToken);
        }
    }
}