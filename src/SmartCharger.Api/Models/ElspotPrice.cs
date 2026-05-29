namespace SmartCharger.Api.Models;

public record ElspotPrice(
    DateTime HourStart,
    double PriceDKK,
    string PriceArea
);

public record ChargeRecommendation(
    DateTime HourStart,
    double PriceDKK,
    bool IsRecommended
);

public record ChargeWindow(
    DateTime WindowStart,
    DateTime WindowEnd,
    double TotalCostDKK,
    double AveragePriceDKK,
    List<ChargeRecommendation> Hours
);
