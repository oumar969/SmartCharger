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

    // Backoff per area: don't retry Energinet for 6 minutes after a 429
    private static readonly Dictionary<string, DateTime> _priceBackoff = new();
    private static readonly Dictionary<string, DateTime> _co2Backoff   = new();

    // Disk cache paths — survives API restarts
    private static string PriceCachePath(string area) => Path.Combine(Path.GetTempPath(), $"sc_price_{area}.json");
    private static string Co2CachePath(string area)   => Path.Combine(Path.GetTempPath(), $"sc_co2_{area}.json");

    private static List<ElspotPrice>? LoadPriceFromDisk(string area)
    {
        try
        {
            var path = PriceCachePath(area);
            if (!File.Exists(path)) return null;
            var (at, data) = JsonSerializer.Deserialize<(DateTime, List<ElspotPrice>)>(File.ReadAllText(path));
            if ((DateTime.UtcNow - at).TotalMinutes > 240) return null; // expired
            _priceCache[area] = (at, data);
            return data;
        }
        catch { return null; }
    }

    private static void SavePriceToDisk(string area, DateTime at, List<ElspotPrice> data)
    {
        try { File.WriteAllText(PriceCachePath(area), JsonSerializer.Serialize((at, data))); }
        catch { }
    }

    private static List<Co2Forecast>? LoadCo2FromDisk(string area)
    {
        try
        {
            var path = Co2CachePath(area);
            if (!File.Exists(path)) return null;
            var (at, data) = JsonSerializer.Deserialize<(DateTime, List<Co2Forecast>)>(File.ReadAllText(path));
            if ((DateTime.UtcNow - at).TotalMinutes > 240) return null;
            _co2Cache[area] = (at, data);
            return data;
        }
        catch { return null; }
    }

    private static void SaveCo2ToDisk(string area, DateTime at, List<Co2Forecast> data)
    {
        try { File.WriteAllText(Co2CachePath(area), JsonSerializer.Serialize((at, data))); }
        catch { }
    }

    // --- Public API ---

    public async Task<List<ElspotPrice>> GetTodayPricesAsync(string priceArea = "DK2")
    {
        if (_priceCache.TryGetValue(priceArea, out var c) && Age(c.At) < 240) return c.Data;
        // Try disk cache before hitting Energinet
        var disk = LoadPriceFromDisk(priceArea);
        if (disk is not null) return disk;
        if (_priceBackoff.TryGetValue(priceArea, out var pb) && DateTime.UtcNow < pb)
        {
            logger.LogWarning("Elspot rate-limited for {Area}, backing off until {Until}", priceArea, pb);
            return c.Data ?? [];
        }

        var filter = Uri.EscapeDataString($"{{\"PriceArea\":\"{priceArea}\"}}");
        var url    = $"{ElspotUrl}?filter={filter}&sort=HourUTC%20desc&limit=48";

        return await FetchAndCache(url, _priceCache, priceArea, c.Data);
    }

    public async Task<List<Co2Forecast>> GetCo2ForecastAsync(string priceArea = "DK2", DateTime? from = null, DateTime? to = null)
    {
        if (_co2Cache.TryGetValue(priceArea, out var c) && Age(c.At) < 240) return c.Data;
        var diskCo2 = LoadCo2FromDisk(priceArea);
        if (diskCo2 is not null) return diskCo2;
        if (_co2Backoff.TryGetValue(priceArea, out var cb) && DateTime.UtcNow < cb)
        {
            logger.LogWarning("CO2 rate-limited for {Area}, backing off until {Until}", priceArea, cb);
            return c.Data ?? [];
        }

        var filter = Uri.EscapeDataString($"{{\"PriceArea\":\"{priceArea}\"}}");

        // If we know the price date range, fetch CO2 for exactly that period
        var url = from.HasValue && to.HasValue
            ? $"{EmissionsUrl}?start={from.Value:yyyy-MM-ddTHH:mm}&end={to.Value:yyyy-MM-ddTHH:mm}&filter={filter}&sort=Minutes5UTC%20asc&limit=576"
            : $"{EmissionsUrl}?filter={filter}&sort=Minutes5UTC%20desc&limit=576";

        return await FetchCo2AndCache(url, _co2Cache, priceArea, c.Data);
    }

    public async Task<List<HourData>> GetMergedAsync(string priceArea = "DK2")
    {
        var prices = await GetTodayPricesAsync(priceArea);

        // Fetch CO2 for the exact same date range as prices
        DateTime? from = prices.Count > 0 ? prices.Min(p => p.HourStart) : null;
        DateTime? to   = prices.Count > 0 ? prices.Max(p => p.HourStart).AddHours(1) : null;
        var co2 = await GetCo2ForecastAsync(priceArea, from, to);

        // CO2 is in 5-min intervals — average per hour
        var co2ByHour = co2
            .GroupBy(c => new DateTime(c.HourStart.Year, c.HourStart.Month, c.HourStart.Day,
                                       c.HourStart.Hour, 0, 0, DateTimeKind.Unspecified))
            .ToDictionary(g => g.Key, g => g.Average(x => x.Co2PerKwh));

        return prices.Select(p => {
            var key    = new DateTime(p.HourStart.Year, p.HourStart.Month, p.HourStart.Day,
                                      p.HourStart.Hour, 0, 0, DateTimeKind.Unspecified);
            var co2val = co2ByHour.TryGetValue(key, out var v) ? v : 0;

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
            SavePriceToDisk(key, DateTime.UtcNow, data);
            return data;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            _priceBackoff[key] = DateTime.UtcNow.AddMinutes(6);
            logger.LogWarning("Elspot 429 for {Area} — backing off for 6 minutes", key);
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
            SaveCo2ToDisk(key, DateTime.UtcNow, data);
            return data;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            _co2Backoff[key] = DateTime.UtcNow.AddMinutes(6);
            logger.LogWarning("CO2 429 for {Area} — backing off for 6 minutes", key);
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
