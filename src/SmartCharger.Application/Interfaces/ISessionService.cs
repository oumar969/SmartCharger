using SmartCharger.Domain.Models;

namespace SmartCharger.Application.Interfaces;

public interface ISessionService
{
    Task<ChargeSession>      SaveAsync(SaveSessionRequest req);
    Task<SessionStats>       GetStatsAsync();
    Task<List<MonthlyStats>> GetMonthlyStatsAsync();
    Task<Co2Report?>         GetCo2ReportAsync(string? month);
}

public record SaveSessionRequest(
    DateTime WindowStart, DateTime WindowEnd, int Hours,
    double AvgPriceDKK, double PeakPriceDKK, double AvgCo2,
    string PriceArea, OptimizationStrategy Strategy);
