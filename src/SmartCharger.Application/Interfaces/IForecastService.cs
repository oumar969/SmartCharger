using SmartCharger.Domain.Models;

namespace SmartCharger.Application.Interfaces;

public interface IForecastService
{
    Task<List<PriceForecast>> GetForecastAsync(string priceArea, int horizonHours);
}
