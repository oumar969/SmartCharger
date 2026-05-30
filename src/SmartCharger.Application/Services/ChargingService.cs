using SmartCharger.Application.Interfaces;
using SmartCharger.Domain.Logic;
using SmartCharger.Domain.Models;

namespace SmartCharger.Application.Services;

public class ChargingService(IElspotRepository repo) : IChargingService
{
    public async Task<List<HourData>> GetMergedAsync(string priceArea)
    {
        var now    = DateTime.UtcNow;
        var from   = now.Date.AddDays(-1);
        var to     = now.Date.AddDays(2);

        var prices = await repo.GetPricesAsync(priceArea, from, to);
        var co2    = await repo.GetCo2Async(priceArea, from, to);

        var co2ByHour = co2
            .GroupBy(c => new DateTime(c.HourStart.Year, c.HourStart.Month,
                                       c.HourStart.Day, c.HourStart.Hour, 0, 0,
                                       DateTimeKind.Unspecified))
            .ToDictionary(g => g.Key, g => g.Average(x => x.Co2PerKwh));

        return prices.Select(p =>
        {
            var key    = new DateTime(p.HourStart.Year, p.HourStart.Month,
                                      p.HourStart.Day, p.HourStart.Hour, 0, 0,
                                      DateTimeKind.Unspecified);
            var co2val = co2ByHour.TryGetValue(key, out var v) ? v : 0;
            return new HourData(p.HourStart, p.PriceDKK, Math.Round(co2val, 1), p.PriceArea);
        }).ToList();
    }

    public async Task<List<ChargeRecommendation>> GetRecommendationsAsync(
        int hours, string priceArea, OptimizationStrategy strategy)
    {
        var merged = await GetMergedAsync(priceArea);
        return ChargingOptimizer.MarkRecommended(merged, hours, strategy);
    }

    public async Task<ChargeWindow?> GetBestWindowAsync(
        int hours, string priceArea, DateTime? deadline, OptimizationStrategy strategy)
    {
        var merged   = await GetMergedAsync(priceArea);
        if (merged.Count == 0) return null;

        var dataStart = merged.Min(h => h.HourStart);
        var dataEnd   = merged.Max(h => h.HourStart).AddHours(1);
        var now       = dataStart;
        var cutoff    = deadline.HasValue && deadline.Value > dataStart && deadline.Value <= dataEnd
                        ? deadline.Value : dataEnd;

        return ChargingOptimizer.FindBestWindow(merged, hours, now, cutoff, strategy);
    }
}
