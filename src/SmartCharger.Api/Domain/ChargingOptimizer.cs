using SmartCharger.Api.Models;

namespace SmartCharger.Api.Domain;

public static class ChargingOptimizer
{
    /// <summary>
    /// Finds the cheapest or greenest contiguous window of N hours before the deadline.
    /// Pure function — no HTTP, no side-effects, fully testable.
    /// </summary>
    public static ChargeWindow? FindBestWindow(
        IReadOnlyList<HourData> hours,
        int hoursNeeded,
        DateTime now,
        DateTime deadline,
        OptimizationStrategy strategy)
    {
        if (hours.Count == 0 || hoursNeeded <= 0) return null;

        var candidates = hours
            .Where(h => h.HourStart >= now && h.HourStart.AddHours(1) <= deadline)
            .OrderBy(h => h.HourStart)
            .ToList();

        if (candidates.Count < hoursNeeded) return null;

        ChargeWindow? best = null;

        for (int i = 0; i <= candidates.Count - hoursNeeded; i++)
        {
            var window = candidates.Skip(i).Take(hoursNeeded).ToList();

            // Verify contiguous
            bool contiguous = true;
            for (int j = 1; j < window.Count; j++)
                if ((window[j].HourStart - window[j - 1].HourStart).TotalHours != 1)
                { contiguous = false; break; }

            if (!contiguous) continue;

            var score = strategy == OptimizationStrategy.Cheapest
                ? window.Sum(h => h.PriceDKK)
                : window.Sum(h => h.Co2PerKwh);

            var bestScore = strategy == OptimizationStrategy.Cheapest
                ? best?.TotalCostDKK
                : best?.AverageCo2 * hoursNeeded;

            if (best is null || score < bestScore)
            {
                best = new ChargeWindow(
                    window.First().HourStart,
                    window.Last().HourStart.AddHours(1),
                    Math.Round(window.Sum(h => h.PriceDKK), 4),
                    Math.Round(window.Average(h => h.PriceDKK), 4),
                    Math.Round(window.Average(h => h.Co2PerKwh), 2),
                    window.Select(h => new ChargeRecommendation(
                        h.HourStart, h.PriceDKK, h.Co2PerKwh, true)).ToList()
                );
            }
        }

        return best;
    }

    /// <summary>
    /// Marks which individual hours are in the cheapest/greenest N (not necessarily contiguous).
    /// </summary>
    public static List<ChargeRecommendation> MarkRecommended(
        IReadOnlyList<HourData> hours,
        int hoursNeeded,
        OptimizationStrategy strategy)
    {
        if (hours.Count == 0) return [];

        var ordered = strategy == OptimizationStrategy.Cheapest
            ? hours.OrderBy(h => h.PriceDKK)
            : hours.OrderBy(h => h.Co2PerKwh);

        var threshold = ordered.Take(hoursNeeded).Last();

        double cutoff = strategy == OptimizationStrategy.Cheapest
            ? threshold.PriceDKK
            : threshold.Co2PerKwh;

        return hours.Select(h => new ChargeRecommendation(
            h.HourStart,
            h.PriceDKK,
            h.Co2PerKwh,
            strategy == OptimizationStrategy.Cheapest
                ? h.PriceDKK <= cutoff
                : h.Co2PerKwh <= cutoff
        )).ToList();
    }
}
