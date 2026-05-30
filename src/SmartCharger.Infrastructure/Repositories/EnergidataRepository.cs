using Microsoft.Extensions.Logging;
using SmartCharger.Application.Interfaces;
using SmartCharger.Domain.Models;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartCharger.Infrastructure.Repositories;

public class EnergidataRepository(HttpClient http, ILogger<EnergidataRepository> logger) : IElspotRepository
{
    private const string ElspotUrl    = "https://api.energidataservice.dk/dataset/Elspotprices";
    private const string EmissionsUrl = "https://api.energidataservice.dk/dataset/CO2Emis";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Disk cache — survives restarts
    private static readonly Dictionary<string, (DateTime At, List<ElspotPrice> Data)>  _priceCache  = new();
    private static readonly Dictionary<string, (DateTime At, List<Co2Forecast> Data)>  _co2Cache    = new();
    private static readonly Dictionary<string, DateTime>                                _priceBackoff = new();
    private static readonly Dictionary<string, DateTime>                                _co2Backoff   = new();

    private static string PriceCachePath(string area) => Path.Combine(Path.GetTempPath(), $"sc_price_{area}.json");
    private static string Co2CachePath(string area)   => Path.Combine(Path.GetTempPath(), $"sc_co2_{area}.json");

    public async Task<List<ElspotPrice>> GetPricesAsync(string priceArea, DateTime from, DateTime to)
    {
        if (_priceCache.TryGetValue(priceArea, out var c) && Age(c.At) < 240) return c.Data;

        // Try disk cache
        var disk = LoadFromDisk<ElspotPrice>(PriceCachePath(priceArea));
        if (disk is not null) { _priceCache[priceArea] = (DateTime.UtcNow, disk); return disk; }

        if (_priceBackoff.TryGetValue(priceArea, out var pb) && DateTime.UtcNow < pb)
            return c.Data ?? [];

        var filter = Uri.EscapeDataString($"{{\"PriceArea\":\"{priceArea}\"}}");
        var url    = $"{ElspotUrl}?filter={filter}&sort=HourUTC%20desc&limit=48";

        try
        {
            var json     = await http.GetStringAsync(url);
            var response = JsonSerializer.Deserialize<EnergidataResponse<ElspotRecord>>(json, JsonOptions);
            var data     = response?.Records
                .Select(r => new ElspotPrice(r.HourUTC, Math.Round(r.SpotPriceDKK / 1000.0, 4), r.PriceArea))
                .OrderBy(x => x.HourStart).ToList() ?? [];
            _priceCache[priceArea] = (DateTime.UtcNow, data);
            SaveToDisk(PriceCachePath(priceArea), data);
            return data;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _priceBackoff[priceArea] = DateTime.UtcNow.AddMinutes(6);
            logger.LogWarning("Elspot 429 for {Area} — backing off 6 min", priceArea);
            return c.Data ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch prices for {Area}", priceArea);
            return c.Data ?? [];
        }
    }

    public async Task<List<Co2Forecast>> GetCo2Async(string priceArea, DateTime from, DateTime to)
    {
        if (_co2Cache.TryGetValue(priceArea, out var c) && Age(c.At) < 240) return c.Data;

        var disk = LoadFromDisk<Co2Forecast>(Co2CachePath(priceArea));
        if (disk is not null) { _co2Cache[priceArea] = (DateTime.UtcNow, disk); return disk; }

        if (_co2Backoff.TryGetValue(priceArea, out var cb) && DateTime.UtcNow < cb)
            return c.Data ?? [];

        var filter = Uri.EscapeDataString($"{{\"PriceArea\":\"{priceArea}\"}}");
        var fromStr = from.ToString("yyyy-MM-ddTHH:mm");
        var toStr   = to.ToString("yyyy-MM-ddTHH:mm");
        var url     = $"{EmissionsUrl}?start={fromStr}&end={toStr}&filter={filter}&sort=Minutes5UTC%20asc&limit=576";

        try
        {
            var json     = await http.GetStringAsync(url);
            var response = JsonSerializer.Deserialize<EnergidataResponse<Co2Record>>(json, JsonOptions);
            var data     = response?.Records
                .Select(r => new Co2Forecast(r.Minutes5UTC, r.CO2Emission, r.PriceArea))
                .OrderBy(x => x.HourStart).ToList() ?? [];
            _co2Cache[priceArea] = (DateTime.UtcNow, data);
            SaveToDisk(Co2CachePath(priceArea), data);
            return data;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _co2Backoff[priceArea] = DateTime.UtcNow.AddMinutes(6);
            logger.LogWarning("CO2 429 for {Area} — backing off 6 min", priceArea);
            return c.Data ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch CO2 for {Area}", priceArea);
            return c.Data ?? [];
        }
    }

    private static double Age(DateTime at) => (DateTime.UtcNow - at).TotalMinutes;

    private static List<T>? LoadFromDisk<T>(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var (at, data) = JsonSerializer.Deserialize<(DateTime, List<T>)>(File.ReadAllText(path));
            return (DateTime.UtcNow - at).TotalMinutes > 240 ? null : data;
        }
        catch { return null; }
    }

    private static void SaveToDisk<T>(string path, List<T> data)
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize((DateTime.UtcNow, data))); }
        catch { }
    }
}

file class EnergidataResponse<T> { [JsonPropertyName("records")] public List<T> Records { get; set; } = []; }

file class ElspotRecord
{
    [JsonPropertyName("HourUTC")]      public DateTime HourUTC      { get; set; }
    [JsonPropertyName("SpotPriceDKK")] public double   SpotPriceDKK { get; set; }
    [JsonPropertyName("PriceArea")]    public string   PriceArea    { get; set; } = "";
}

file class Co2Record
{
    [JsonPropertyName("Minutes5UTC")] public DateTime Minutes5UTC { get; set; }
    [JsonPropertyName("CO2Emission")] public double   CO2Emission { get; set; }
    [JsonPropertyName("PriceArea")]   public string   PriceArea   { get; set; } = "";
}
