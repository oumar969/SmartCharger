namespace SmartCharger.Domain.Models;

public class ChargeSession
{
    public int      Id           { get; set; }
    public DateTime WindowStart  { get; set; }
    public DateTime WindowEnd    { get; set; }
    public int      Hours        { get; set; }
    public double   AvgPriceDKK  { get; set; }
    public double   PeakPriceDKK { get; set; }
    public double   SavingsDKK   { get; set; }
    public double   AvgCo2       { get; set; }
    public string   PriceArea    { get; set; } = "";
    public OptimizationStrategy Strategy { get; set; }
    public DateTime SavedAt      { get; set; } = DateTime.UtcNow;
}

public record SessionStats(int TotalSessions, double TotalSavingsDKK, double TotalCo2Saved, double AvgSavingPerSession);
public record MonthlyStats(string Month, double SavingsDKK, double Co2Saved, int Sessions);
public record Co2Report(string Month, int TotalHours, double AvgCo2, double GreenPct, double Co2SavedGrams, int TreesEquivalent, string ShareText);
public record PriceForecast(DateTime HourStart, double ForecastedPriceDKK, double LowerBound, double UpperBound);
