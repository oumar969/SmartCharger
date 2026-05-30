using SmartCharger.Application.Interfaces;

namespace SmartCharger.Api.Services;

public class CacheWarmupService(IServiceProvider services, ILogger<CacheWarmupService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), ct);

        foreach (var area in new[] { "DK2", "DK1" })
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                using var scope = services.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<IChargingService>();
                await svc.GetMergedAsync(area);
                logger.LogInformation("Cache warmed for {Area}", area);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cache warmup failed for {Area}", area);
            }
            await Task.Delay(TimeSpan.FromSeconds(8), ct);
        }
    }
}
