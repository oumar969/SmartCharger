using SmartCharger.Domain.Models;

namespace SmartCharger.Application.Interfaces;

public interface ISessionRepository
{
    Task<ChargeSession> SaveAsync(ChargeSession session);
    Task<List<ChargeSession>> GetAllAsync();
    Task<List<ChargeSession>> GetByMonthAsync(string month);
}
