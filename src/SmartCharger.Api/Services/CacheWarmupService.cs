using SmartCharger.Api.Services;

namespace SmartCharger.Api.Services;

/// <summary>
/// Fetches DK1 + DK2 price and CO2 data on startup with a staggered delay
/// so both areas are cached before the first user request.
/// </summary>
public class CacheWarmupService(IServiceProvider services, ILogger<CacheWarmupService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Wait for app to finish starting
        await Task.Delay(TimeSpan.FromSeconds(3), ct);

        foreach (var area in new[] { "DK2", "DK1" })
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                using var scope  = services.CreateScope();
                var elspot       = scope.ServiceProvider.GetRequiredService<ElspotService>();
                await elspot.GetMergedAsync(area);
                logger.LogInformation("Cache warmed for {Area}", area);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cache warmup failed for {Area}", area);
            }
            // Stagger requests so we don't hit rate limit for both areas simultaneously
            await Task.Delay(TimeSpan.FromSeconds(8), ct);
        }
    }
}
