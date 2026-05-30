using SmartCharger.Domain.Models;

namespace SmartCharger.Application.Interfaces;

public interface IElspotRepository
{
    Task<List<ElspotPrice>> GetPricesAsync(string priceArea, DateTime from, DateTime to);
    Task<List<Co2Forecast>> GetCo2Async(string priceArea, DateTime from, DateTime to);
}
