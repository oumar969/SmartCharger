using SmartCharger.Api.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartCharger.Api.Services;

public class ElspotService(HttpClient http, ILogger<ElspotService> logger)
{
    private const string BaseUrl = "https://api.energidataservice.dk/dataset/Elspotprices";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Dictionary<string, (DateTime FetchedAt, List<ElspotPrice> Prices)> _cache = new();

    public async Task<List<ElspotPrice>> GetTodayPricesAsync(string priceArea = "DK2")
    {
        if (_cache.TryGetValue(priceArea, out var cached) &&
            (DateTime.UtcNow - cached.FetchedAt).TotalMinutes < 60)
            return cached.Prices;

        var from = DateTime.UtcNow.Date;
        var to = from.AddDays(2);
        var url = $"{BaseUrl}?start={from:yyyy-MM-ddTHH:mm}&end={to:yyyy-MM-ddTHH:mm}&filter={{\"PriceArea\":\"{priceArea}\"}}&sort=HourUTC asc&limit=48";

        try
        {
            var json = await http.GetStringAsync(url);
            var response = JsonSerializer.Deserialize<EnergidataResponse>(json, JsonOptions);
            var prices = response?.Records.Select(r => new ElspotPrice(
                r.HourUTC,
                Math.Round(r.SpotPriceDKK / 1000.0, 4),
                r.PriceArea
            )).ToList() ?? [];
            _cache[priceArea] = (DateTime.UtcNow, prices);
            return prices;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch Elspot prices");
            return cached.Prices ?? [];
        }
    }

    // Finds the cheapest contiguous window of N hours before a deadline
    public async Task<ChargeWindow?> GetBestWindowAsync(
        int hoursNeeded = 4, string priceArea = "DK2", DateTime? deadline = null)
    {
        var prices = await GetTodayPricesAsync(priceArea);
        if (prices.Count == 0) return null;

        var now = DateTime.UtcNow;
        var cutoff = deadline ?? now.Date.AddDays(1).AddHours(7); // default: tomorrow 07:00

        var candidates = prices
            .Where(p => p.HourStart >= now && p.HourStart.AddHours(1) <= cutoff)
            .OrderBy(p => p.HourStart)
            .ToList();

        if (candidates.Count < hoursNeeded) return null;

        // Sliding window over contiguous hours
        ChargeWindow? best = null;
        for (int i = 0; i <= candidates.Count - hoursNeeded; i++)
        {
            var window = candidates.Skip(i).Take(hoursNeeded).ToList();

            // Verify hours are actually contiguous
            bool contiguous = true;
            for (int j = 1; j < window.Count; j++)
                if ((window[j].HourStart - window[j - 1].HourStart).TotalHours != 1)
                { contiguous = false; break; }

            if (!contiguous) continue;

            var total = window.Sum(p => p.PriceDKK);
            if (best is null || total < best.TotalCostDKK)
            {
                best = new ChargeWindow(
                    window.First().HourStart,
                    window.Last().HourStart.AddHours(1),
                    Math.Round(total, 4),
                    Math.Round(total / hoursNeeded, 4),
                    window.Select(p => new ChargeRecommendation(p.HourStart, p.PriceDKK, true)).ToList()
                );
            }
        }

        return best;
    }

    // Legacy: cheapest N hours (not necessarily contiguous)
    public async Task<List<ChargeRecommendation>> GetChargeRecommendationsAsync(
        int hoursNeeded = 4, string priceArea = "DK2")
    {
        var prices = await GetTodayPricesAsync(priceArea);
        if (prices.Count == 0) return [];

        var threshold = prices.OrderBy(p => p.PriceDKK)
                               .Take(hoursNeeded)
                               .Max(p => p.PriceDKK);

        return prices.Select(p => new ChargeRecommendation(
            p.HourStart, p.PriceDKK, p.PriceDKK <= threshold
        )).ToList();
    }
}

file class EnergidataResponse
{
    [JsonPropertyName("records")]
    public List<ElspotRecord> Records { get; set; } = [];
}

file class ElspotRecord
{
    [JsonPropertyName("HourUTC")]
    public DateTime HourUTC { get; set; }

    [JsonPropertyName("SpotPriceDKK")]
    public double SpotPriceDKK { get; set; }

    [JsonPropertyName("PriceArea")]
    public string PriceArea { get; set; } = "";
}
