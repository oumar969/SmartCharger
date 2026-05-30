namespace SmartCharger.Domain.Models;

public record ElspotPrice(DateTime HourStart, double PriceDKK, string PriceArea);
public record Co2Forecast(DateTime HourStart, double Co2PerKwh, string PriceArea);
public record HourData(DateTime HourStart, double PriceDKK, double Co2PerKwh, string PriceArea);
public record ChargeRecommendation(DateTime HourStart, double PriceDKK, double Co2PerKwh, bool IsRecommended);
public record ChargeWindow(
    DateTime WindowStart, DateTime WindowEnd,
    double TotalCostDKK, double AveragePriceDKK, double AverageCo2,
    List<ChargeRecommendation> Hours);

public enum OptimizationStrategy { Cheapest, Greenest }
