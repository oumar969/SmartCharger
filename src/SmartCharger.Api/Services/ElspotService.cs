using SmartCharger.Api.Domain;
using SmartCharger.Api.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartCharger.Api.Services;

public class ElspotService(HttpClient http, ILogger<ElspotService> logger)
{
    private const string ElspotUrl  = "https://api.energidataservice.dk/dataset/Elspotprices";
    private const string EmissionsUrl = "https://api.energidataservice.dk/dataset/CO2Emis";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly Dictionary<string, (DateTime At, List<ElspotPrice> Data)>  _priceCache = new();
    private static readonly Dictionary<string, (DateTime At, List<Co2Forecast> Data)>  _co2Cache   = new();

    // Backoff: don't retry Energinet for 6 minutes after a 429
    private static DateTime _priceBackoffUntil = DateTime.MinValue;
    private static DateTime _co2BackoffUntil   = DateTime.MinValue;

    // --- Public API ---

    public async Task<List<ElspotPrice>> GetTodayPricesAsync(string priceArea = "DK2")
    {
        if (_priceCache.TryGetValue(priceArea, out var c) && Age(c.At) < 240) return c.Data;
        if (DateTime.UtcNow < _priceBackoffUntil)
        {
            logger.LogWarning("Elspot rate-limited, backing off until {Until}", _priceBackoffUntil);
            return c.Data ?? [];
        }

        var filter = Uri.EscapeDataString($"{{\"PriceArea\":\"{priceArea}\"}}");
        var url    = $"{ElspotUrl}?filter={filter}&sort=HourUTC%20desc&limit=48";

        return await FetchAndCache(url, _priceCache, priceArea, c.Data);
    }

    public async Task<List<Co2Forecast>> GetCo2ForecastAsync(string priceArea = "DK2")
    {
        if (_co2Cache.TryGetValue(priceArea, out var c) && Age(c.At) < 240) return c.Data;
        if (DateTime.UtcNow < _co2BackoffUntil)
        {
            logger.LogWarning("CO2 rate-limited, backing off until {Until}", _co2BackoffUntil);
            return c.Data ?? [];
        }

        var filter = Uri.EscapeDataString($"{{\"PriceArea\":\"{priceArea}\"}}");
        var url    = $"{EmissionsUrl}?filter={filter}&sort=Minutes5UTC%20desc&limit=576";

        return await FetchCo2AndCache(url, _co2Cache, priceArea, c.Data);
    }

    public async Task<List<HourData>> GetMergedAsync(string priceArea = "DK2")
    {
        var prices = await GetTodayPricesAsync(priceArea);
        var co2    = await GetCo2ForecastAsync(priceArea);

        // CO2 data is in 5-min intervals — average per hour, strip minutes for matching
        var co2ByHour = co2
            .GroupBy(c => c.HourStart.Date.AddHours(c.HourStart.Hour))
            .ToDictionary(g => g.Key, g => g.Average(x => x.Co2PerKwh));

        return prices.Select(p => {
            // Try both UTC and unspecified DateTimeKind since Energinet may differ
            var key = p.HourStart.Kind == DateTimeKind.Utc
                ? DateTime.SpecifyKind(p.HourStart, DateTimeKind.Unspecified)
                : p.HourStart;
            var keyUtc = p.HourStart.Date.AddHours(p.HourStart.Hour);

            var co2val = co2ByHour.TryGetValue(keyUtc, out var v1) ? v1
                       : co2ByHour.TryGetValue(key, out var v2) ? v2
                       : 0;

            return new HourData(p.HourStart, p.PriceDKK, Math.Round(co2val, 1), p.PriceArea);
        }).ToList();
    }

    public async Task<List<ChargeRecommendation>> GetRecommendationsAsync(
        int hoursNeeded = 4, string priceArea = "DK2",
        OptimizationStrategy strategy = OptimizationStrategy.Cheapest)
    {
        var merged = await GetMergedAsync(priceArea);
        return ChargingOptimizer.MarkRecommended(merged, hoursNeeded, strategy);
    }

    public async Task<ChargeWindow?> GetBestWindowAsync(
        int hoursNeeded = 4, string priceArea = "DK2",
        DateTime? deadline = null,
        OptimizationStrategy strategy = OptimizationStrategy.Cheapest)
    {
        var merged = await GetMergedAsync(priceArea);
        if (merged.Count == 0) return null;

        // Use data's own time range so it works with both live and historical data
        var dataStart = merged.Min(h => h.HourStart);
        var dataEnd   = merged.Max(h => h.HourStart).AddHours(1);
        var now       = dataStart;
        var cutoff    = deadline.HasValue && deadline.Value > dataStart && deadline.Value <= dataEnd
                        ? deadline.Value
                        : dataEnd;

        return ChargingOptimizer.FindBestWindow(merged, hoursNeeded, now, cutoff, strategy);
    }

    // --- Helpers ---

    private static double Age(DateTime at) => (DateTime.UtcNow - at).TotalMinutes;

    private async Task<List<ElspotPrice>> FetchAndCache(
        string url,
        Dictionary<string, (DateTime, List<ElspotPrice>)> cache,
        string key,
        List<ElspotPrice>? fallback)
    {
        try
        {
            var json     = await http.GetStringAsync(url);
            var response = JsonSerializer.Deserialize<EnergidataResponse<ElspotRecord>>(json, JsonOptions);
            var data     = response?.Records.Select(r =>
                new ElspotPrice(r.HourUTC, Math.Round(r.SpotPriceDKK / 1000.0, 4), r.PriceArea)
            ).OrderBy(x => x.HourStart).ToList() ?? [];
            cache[key]   = (DateTime.UtcNow, data);
            return data;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            _priceBackoffUntil = DateTime.UtcNow.AddMinutes(6);
            logger.LogWarning("Elspot 429 — backing off for 6 minutes");
            return fallback ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch Elspot prices for {Area}", key);
            return fallback ?? [];
        }
    }

    private async Task<List<Co2Forecast>> FetchCo2AndCache(
        string url,
        Dictionary<string, (DateTime, List<Co2Forecast>)> cache,
        string key,
        List<Co2Forecast>? fallback)
    {
        try
        {
            var json     = await http.GetStringAsync(url);
            var response = JsonSerializer.Deserialize<EnergidataResponse<Co2Record>>(json, JsonOptions);
            var data     = response?.Records.Select(r => new Co2Forecast(r.Minutes5UTC, r.CO2Emission, r.PriceArea)).OrderBy(x => x.HourStart).ToList() ?? [];
            cache[key]   = (DateTime.UtcNow, data);
            return data;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            _co2BackoffUntil = DateTime.UtcNow.AddMinutes(6);
            logger.LogWarning("CO2 429 — backing off for 6 minutes");
            return fallback ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch CO2 forecast for {Area}", key);
            return fallback ?? [];
        }
    }
}

// Energidataservice shapes
file class EnergidataResponse<T> { [JsonPropertyName("records")] public List<T> Records { get; set; } = []; }

file class ElspotRecord
{
    [JsonPropertyName("HourUTC")]     public DateTime HourUTC      { get; set; }
    [JsonPropertyName("SpotPriceDKK")] public double  SpotPriceDKK { get; set; }
    [JsonPropertyName("PriceArea")]   public string   PriceArea    { get; set; } = "";
}

file class Co2Record
{
    [JsonPropertyName("Minutes5UTC")]  public DateTime Minutes5UTC { get; set; }
    [JsonPropertyName("CO2Emission")]  public double   CO2Emission { get; set; }
    [JsonPropertyName("PriceArea")]    public string   PriceArea   { get; set; } = "";
}
