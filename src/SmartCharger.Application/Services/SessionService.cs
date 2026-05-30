using SmartCharger.Application.Interfaces;
using SmartCharger.Domain.Models;

namespace SmartCharger.Application.Services;

public class SessionService(ISessionRepository repo) : ISessionService
{
    public async Task<ChargeSession> SaveAsync(SaveSessionRequest req)
    {
        var session = new ChargeSession
        {
            WindowStart  = req.WindowStart,
            WindowEnd    = req.WindowEnd,
            Hours        = req.Hours,
            AvgPriceDKK  = req.AvgPriceDKK,
            PeakPriceDKK = req.PeakPriceDKK,
            SavingsDKK   = Math.Round((req.PeakPriceDKK - req.AvgPriceDKK) * req.Hours, 4),
            AvgCo2       = req.AvgCo2,
            PriceArea    = req.PriceArea,
            Strategy     = req.Strategy,
            SavedAt      = DateTime.UtcNow
        };
        return await repo.SaveAsync(session);
    }

    public async Task<SessionStats> GetStatsAsync()
    {
        var sessions = await repo.GetAllAsync();
        if (sessions.Count == 0) return new SessionStats(0, 0, 0, 0);

        return new SessionStats(
            sessions.Count,
            Math.Round(sessions.Sum(s => s.SavingsDKK), 2),
            Math.Round(sessions.Sum(s => s.Hours * Math.Max(0, 100 - s.AvgCo2)), 0),
            Math.Round(sessions.Sum(s => s.SavingsDKK) / sessions.Count, 2)
        );
    }

    public async Task<List<MonthlyStats>> GetMonthlyStatsAsync()
    {
        var sessions = await repo.GetAllAsync();
        return sessions
            .GroupBy(s => s.SavedAt.ToString("yyyy-MM"))
            .OrderBy(g => g.Key)
            .Select(g => new MonthlyStats(
                g.Key,
                Math.Round(g.Sum(s => s.SavingsDKK), 2),
                Math.Round(g.Sum(s => s.Hours * Math.Max(0, 100 - s.AvgCo2)), 0),
                g.Count()
            )).ToList();
    }

    public async Task<Co2Report?> GetCo2ReportAsync(string? month = null)
    {
        var target   = month ?? DateTime.UtcNow.ToString("yyyy-MM");
        var sessions = await repo.GetByMonthAsync(target);
        if (sessions.Count == 0) return null;

        var totalHours = sessions.Sum(s => s.Hours);
        var avgCo2     = sessions.Average(s => s.AvgCo2);
        var greenHours = sessions.Sum(s => s.AvgCo2 < 100 ? s.Hours : 0);
        var greenPct   = totalHours > 0 ? Math.Round((double)greenHours / totalHours * 100, 0) : 0;
        var co2Saved   = Math.Round(sessions.Sum(s => s.Hours * Math.Max(0, 100 - s.AvgCo2)), 0);
        var trees      = (int)Math.Floor(co2Saved / 1750.0);
        var monthName  = DateTime.ParseExact(target, "yyyy-MM", null)
                         .ToString("MMMM yyyy", new System.Globalization.CultureInfo("da-DK"));
        var shareText  = $"🌱 Jeg ladede min elbil {greenPct}% på grøn energi i {monthName} via SmartCharger " +
                         $"og sparede {co2Saved:N0}g CO₂ — svarende til {trees} træer! #SmartCharger #GrønEnergi #Elbil";

        return new Co2Report(target, totalHours, Math.Round(avgCo2, 1),
                             greenPct, co2Saved, trees, shareText);
    }
}
