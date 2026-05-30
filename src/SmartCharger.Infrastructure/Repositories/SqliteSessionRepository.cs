using Microsoft.EntityFrameworkCore;
using SmartCharger.Application.Interfaces;
using SmartCharger.Domain.Models;
using SmartCharger.Infrastructure.Persistence;

namespace SmartCharger.Infrastructure.Repositories;

public class SqliteSessionRepository(AppDbContext db) : ISessionRepository
{
    public async Task<ChargeSession> SaveAsync(ChargeSession session)
    {
        db.ChargeSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    public async Task<List<ChargeSession>> GetAllAsync()
        => await db.ChargeSessions.OrderByDescending(s => s.SavedAt).ToListAsync();

    public async Task<List<ChargeSession>> GetByMonthAsync(string month)
        => await db.ChargeSessions
            .Where(s => s.SavedAt.ToString("yyyy-MM") == month)
            .ToListAsync();
}
