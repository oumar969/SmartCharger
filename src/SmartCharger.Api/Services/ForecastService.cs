using Microsoft.ML;
using Microsoft.ML.Transforms.TimeSeries;
using SmartCharger.Api.Models;
using SmartCharger.Api.Services;

namespace SmartCharger.Api.Services;

public record PriceForecast(DateTime HourStart, double ForecastedPriceDKK, double LowerBound, double UpperBound);

public class ForecastService(ElspotService elspot, ILogger<ForecastService> logger)
{
    private static readonly MLContext _ml = new(seed: 42);

    public async Task<List<PriceForecast>> GetForecastAsync(string priceArea = "DK2", int horizonHours = 24)
    {
        // Fetch historical prices to train on
        var prices = await elspot.GetTodayPricesAsync(priceArea);
        if (prices.Count < 24)
        {
            logger.LogWarning("Not enough historical data for forecast ({Count} hours)", prices.Count);
            return [];
        }

        try
        {
            // Prepare training data
            var data = prices.Select(p => new PriceInput { Price = (float)p.PriceDKK }).ToList();
            var trainData = _ml.Data.LoadFromEnumerable(data);

            // SSA (Singular Spectrum Analysis) forecasting
            var pipeline = _ml.Forecasting.ForecastBySsa(
                outputColumnName:    nameof(PriceOutput.ForecastedPrices),
                inputColumnName:     nameof(PriceInput.Price),
                windowSize:          12,
                seriesLength:        prices.Count,
                trainSize:           prices.Count,
                horizon:             horizonHours,
                confidenceLevel:     0.95f,
                confidenceLowerBoundColumn: nameof(PriceOutput.LowerBound),
                confidenceUpperBoundColumn: nameof(PriceOutput.UpperBound)
            );

            var model      = pipeline.Fit(trainData);
            var engine     = model.CreateTimeSeriesEngine<PriceInput, PriceOutput>(_ml);
            var prediction = engine.Predict();

            // Build forecast starting from last known hour + 1
            var lastHour = prices.Max(p => p.HourStart);
            return prediction.ForecastedPrices
                .Select((price, i) => new PriceForecast(
                    lastHour.AddHours(i + 1),
                    Math.Max(0, Math.Round(price, 4)),
                    Math.Max(0, Math.Round(prediction.LowerBound[i], 4)),
                    Math.Max(0, Math.Round(prediction.UpperBound[i], 4))
                ))
                .ToList();
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
