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

    // --- Public API ---

    public async Task<List<ElspotPrice>> GetTodayPricesAsync(string priceArea = "DK2")
    {
        if (_priceCache.TryGetValue(priceArea, out var c) && Age(c.At) < 240) return c.Data;

        var from   = DateTime.UtcNow.Date;
        var to     = from.AddDays(2);
        var filter = Uri.EscapeDataString($"{{\"PriceArea\":\"{priceArea}\"}}");
        var url    = $"{ElspotUrl}?start={from:yyyy-MM-ddTHH:mm}&end={to:yyyy-MM-ddTHH:mm}&filter={filter}&sort=HourUTC%20asc&limit=48";

        return await FetchAndCache(url, _priceCache, priceArea, c.Data);
    }

    public async Task<List<Co2Forecast>> GetCo2ForecastAsync(string priceArea = "DK2")
    {
        if (_co2Cache.TryGetValue(priceArea, out var c) && Age(c.At) < 240) return c.Data;

        var from   = DateTime.UtcNow.Date;
        var to     = from.AddDays(2);
        var filter = Uri.EscapeDataString($"{{\"PriceArea\":\"{priceArea}\"}}");
        var url    = $"{EmissionsUrl}?start={from:yyyy-MM-ddTHH:mm}&end={to:yyyy-MM-ddTHH:mm}&filter={filter}&sort=Minutes5UTC%20asc&limit=576";

        return await FetchCo2AndCache(url, _co2Cache, priceArea, c.Data);
    }

    public async Task<List<HourData>> GetMergedAsync(string priceArea = "DK2")
    {
        var prices = await GetTodayPricesAsync(priceArea);
        var co2    = await GetCo2ForecastAsync(priceArea);

        // CO2 data is in 5-min intervals — average per hour
        var co2ByHour = co2
            .GroupBy(c => new DateTime(c.HourStart.Year, c.HourStart.Month, c.HourStart.Day, c.HourStart.Hour, 0, 0, DateTimeKind.Utc))
            .ToDictionary(g => g.Key, g => g.Average(x => x.Co2PerKwh));

        return prices.Select(p => new HourData(
            p.HourStart,
            p.PriceDKK,
            co2ByHour.TryGetValue(p.HourStart, out var co2val) ? Math.Round(co2val, 1) : 0,
            p.PriceArea
        )).ToList();
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
        var merged   = await GetMergedAsync(priceArea);
        var cutoff   = deadline ?? DateTime.UtcNow.Date.AddDays(1).AddHours(7);
        return ChargingOptimizer.FindBestWindow(merged, hoursNeeded, DateTime.UtcNow, cutoff, strategy);
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
            ).ToList() ?? [];
            cache[key]   = (DateTime.UtcNow, data);
            return data;
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
            var data     = response?.Records.Select(r => new Co2Forecast(r.Minutes5UTC, r.CO2Emission, r.PriceArea)).ToList() ?? [];
            cache[key]   = (DateTime.UtcNow, data);
            return data;
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
