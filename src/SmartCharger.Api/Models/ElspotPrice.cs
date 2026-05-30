namespace SmartCharger.Api.Models;

public record ElspotPrice(
    DateTime HourStart,
    double PriceDKK,
    string PriceArea
);

public record Co2Forecast(
    DateTime HourStart,
    double Co2PerKwh,  // g CO2/kWh
    string PriceArea
);

public record HourData(
    DateTime HourStart,
    double PriceDKK,
    double Co2PerKwh,
    string PriceArea
);

public record ChargeRecommendation(
    DateTime HourStart,
    double PriceDKK,
    double Co2PerKwh,
    bool IsRecommended
);

public record ChargeWindow(
    DateTime WindowStart,
    DateTime WindowEnd,
    double TotalCostDKK,
    double AveragePriceDKK,
    double AverageCo2,
    List<ChargeRecommendation> Hours
);

public enum OptimizationStrategy { Cheapest, Greenest }

public class ChargeSession
{
    public int      Id          { get; set; }
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd   { get; set; }
    public int      Hours       { get; set; }
    public double   AvgPriceDKK { get; set; }
    public double   PeakPriceDKK { get; set; }   // most expensive hour that day
    public double   SavingsDKK  { get; set; }    // (peak - avg) * hours
    public double   AvgCo2      { get; set; }
    public string   PriceArea   { get; set; } = "";
    public OptimizationStrategy Strategy { get; set; }
    public DateTime SavedAt     { get; set; } = DateTime.UtcNow;
}

public record SessionStats(
    int    TotalSessions,
    double TotalSavingsDKK,
    double TotalCo2Saved,     // vs. peak-hour charging
    double AvgSavingPerSession
);
