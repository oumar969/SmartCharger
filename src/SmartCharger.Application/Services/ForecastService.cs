using Microsoft.ML;
using Microsoft.ML.Transforms.TimeSeries;
using Microsoft.Extensions.Logging;
using SmartCharger.Application.Interfaces;
using SmartCharger.Domain.Models;

namespace SmartCharger.Application.Services;

public class ForecastService(IElspotRepository repo, ILogger<ForecastService> logger) : IForecastService
{
    private static readonly MLContext _ml = new(seed: 42);

    public async Task<List<PriceForecast>> GetForecastAsync(string priceArea = "DK2", int horizonHours = 24)
    {
        var now    = DateTime.UtcNow;
        var prices = await repo.GetPricesAsync(priceArea, now.Date.AddDays(-2), now.Date.AddDays(1));

        if (prices.Count < 24)
        {
            logger.LogWarning("Not enough data for forecast ({Count} hours)", prices.Count);
            return [];
        }

        try
        {
            var data      = prices.Select(p => new PriceInput { Price = (float)p.PriceDKK }).ToList();
            var trainData = _ml.Data.LoadFromEnumerable(data);

            var pipeline = _ml.Forecasting.ForecastBySsa(
                outputColumnName:           nameof(PriceOutput.ForecastedPrices),
                inputColumnName:            nameof(PriceInput.Price),
                windowSize:                 12,
                seriesLength:               prices.Count,
                trainSize:                  prices.Count,
                horizon:                    horizonHours,
                confidenceLevel:            0.95f,
                confidenceLowerBoundColumn: nameof(PriceOutput.LowerBound),
                confidenceUpperBoundColumn: nameof(PriceOutput.UpperBound)
            );

            var model      = pipeline.Fit(trainData);
            var engine     = model.CreateTimeSeriesEngine<PriceInput, PriceOutput>(_ml);
            var prediction = engine.Predict();
            var lastHour   = prices.Max(p => p.HourStart);

            return prediction.ForecastedPrices
                .Select((price, i) => new PriceForecast(
                    lastHour.AddHours(i + 1),
                    Math.Max(0, Math.Round(price, 4)),
                    Math.Max(0, Math.Round(prediction.LowerBound[i], 4)),
                    Math.Max(0, Math.Round(prediction.UpperBound[i], 4))
                )).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Forecast failed");
            return [];
        }
    }
}

file class PriceInput  { public float Price { get; set; } }
file class PriceOutput
{
    public float[] ForecastedPrices { get; set; } = [];
    public float[] LowerBound       { get; set; } = [];
    public float[] UpperBound       { get; set; } = [];
}
