using SmartCharger.Domain.Models;

namespace SmartCharger.Application.Interfaces;

public interface IChargingService
{
    Task<List<HourData>>             GetMergedAsync(string priceArea);
    Task<List<ChargeRecommendation>> GetRecommendationsAsync(int hours, string priceArea, OptimizationStrategy strategy);
    Task<ChargeWindow?>              GetBestWindowAsync(int hours, string priceArea, DateTime? deadline, OptimizationStrategy strategy);
}
