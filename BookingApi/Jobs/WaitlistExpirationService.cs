using BookingApi.Data;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Jobs;

public class WaitlistExpirationService(IServiceScopeFactory scopeFactory, ILogger<WaitlistExpirationService> logger) : BackgroundService
{
    // The sweep itself, separate from the loop so it can be tested with a fixed "now".
    public static Task<int> ExpireStaleEntriesAsync(AppDbContext context, DateTime now, CancellationToken token) =>
        context.WaitlistEntries
            .Where(w => w.Status == WaitlistStatus.Waiting && w.RequestedStartTime <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WaitlistStatus.Expired), token);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var expired = await ExpireStaleEntriesAsync(context, DateTime.UtcNow, stoppingToken);
                if (expired > 0) logger.LogInformation("Expired {Count} waitlist entries", expired);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Waitlist expiration sweep failed.");   // no rethrow: that would stop the host
            }

            await Task.Delay(TimeSpan.FromMinutes(20), stoppingToken);
        }
    }
}