using Microsoft.EntityFrameworkCore;
using SmartCharger.Api.Data;
using SmartCharger.Api.Models;

namespace SmartCharger.Api.Services;

public record SaveSessionRequest(
    DateTime WindowStart,
    DateTime WindowEnd,
    int      Hours,
    double   AvgPriceDKK,
    double   PeakPriceDKK,
    double   AvgCo2,
    string   PriceArea,
    OptimizationStrategy Strategy
);

public class SessionService(AppDbContext db)
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
        db.ChargeSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    public async Task<List<ChargeSession>> GetRecentAsync(int limit = 20)
        => await db.ChargeSessions
            .OrderByDescending(s => s.SavedAt)
            .Take(limit)
            .ToListAsync();

    public async Task<SessionStats> GetStatsAsync()
    {
        var sessions = await db.ChargeSessions.ToListAsync();
        if (sessions.Count == 0)
            return new SessionStats(0, 0, 0, 0);

        var totalSavings   = sessions.Sum(s => s.SavingsDKK);
        // CO2 saved = hours charged at green times vs. avg grid CO2 (assume 100g baseline)
        var totalCo2Saved  = sessions.Sum(s => s.Hours * Math.Max(0, 100 - s.AvgCo2));

        return new SessionStats(
            sessions.Count,
            Math.Round(totalSavings, 2),
            Math.Round(totalCo2Saved, 0),
            Math.Round(totalSavings / sessions.Count, 2)
        );
    }
}
