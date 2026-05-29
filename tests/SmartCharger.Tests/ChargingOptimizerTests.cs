using FluentAssertions;
using SmartCharger.Api.Domain;
using SmartCharger.Api.Models;

namespace SmartCharger.Tests;

public class ChargingOptimizerTests
{
    private static readonly DateTime Now      = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Deadline = new(2026, 1, 2,  7, 0, 0, DateTimeKind.Utc);

    // Build a simple 24-hour dataset starting at 'Now'
    private static List<HourData> BuildHours(double[] prices, double[]? co2 = null)
    {
        return prices.Select((p, i) => new HourData(
            Now.AddHours(i), p,
            co2 is not null ? co2[i] : 100.0,
            "DK2"
        )).ToList();
    }

    [Fact]
    public void FindBestWindow_ReturnsCheapestContiguousBlock()
    {
        var prices = new double[] { 5, 4, 3, 1, 2, 6, 7, 8 };
        var hours  = BuildHours(prices);

        var result = ChargingOptimizer.FindBestWindow(hours, 3, Now, Deadline, OptimizationStrategy.Cheapest);

        result.Should().NotBeNull();
        result!.WindowStart.Should().Be(Now.AddHours(2)); // hours 3,1,2 = sum 6
        result.TotalCostDKK.Should().Be(6);
    }

    [Fact]
    public void FindBestWindow_ReturnsGreenestContiguousBlock()
    {
        var prices = new double[] { 5, 4, 3, 1, 2, 6, 7, 8 };
        var co2    = new double[] { 90, 80, 20, 10, 15, 200, 300, 400 };
        var hours  = BuildHours(prices, co2);

        var result = ChargingOptimizer.FindBestWindow(hours, 3, Now, Deadline, OptimizationStrategy.Greenest);

        result.Should().NotBeNull();
        result!.WindowStart.Should().Be(Now.AddHours(2)); // co2: 20+10+15 = 45 (lowest sum)
    }

    [Fact]
    public void FindBestWindow_ReturnsNull_WhenNotEnoughHours()
    {
        var hours = BuildHours(new double[] { 1, 2 });

        var result = ChargingOptimizer.FindBestWindow(hours, 5, Now, Deadline, OptimizationStrategy.Cheapest);

        result.Should().BeNull();
    }

    [Fact]
    public void FindBestWindow_IgnoresHoursPastDeadline()
    {
        var prices = new double[] { 9, 9, 9, 1, 1, 1, 1 }; // cheapest hours are after deadline
        var hours  = BuildHours(prices);
        var tightDeadline = Now.AddHours(4); // only first 4 hours are valid

        var result = ChargingOptimizer.FindBestWindow(hours, 3, Now, tightDeadline, OptimizationStrategy.Cheapest);

        result.Should().NotBeNull();
        // hours 11+12+13 (9+9+1=19) beats 10+11+12 (9+9+9=27)
        result!.WindowStart.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void FindBestWindow_HandlesNegativePrices()
    {
        var prices = new double[] { -2, -3, -1, 5, 6, 7 };
        var hours  = BuildHours(prices);

        var result = ChargingOptimizer.FindBestWindow(hours, 2, Now, Deadline, OptimizationStrategy.Cheapest);

        result.Should().NotBeNull();
        result!.TotalCostDKK.Should().Be(-5); // -2 + -3
        result.WindowStart.Should().Be(Now);
    }

    [Fact]
    public void MarkRecommended_MarksCorrectHours_Cheapest()
    {
        var hours  = BuildHours(new double[] { 5, 1, 3, 2, 4 });

        var result = ChargingOptimizer.MarkRecommended(hours, 2, OptimizationStrategy.Cheapest);

        result.Where(r => r.IsRecommended).Select(r => r.PriceDKK)
            .Should().BeEquivalentTo(new[] { 1.0, 2.0 });
    }

    [Fact]
    public void MarkRecommended_MarksCorrectHours_Greenest()
    {
        var co2   = new double[] { 300, 50, 100, 20, 200 };
        var hours = BuildHours(new double[] { 1, 1, 1, 1, 1 }, co2);

        var result = ChargingOptimizer.MarkRecommended(hours, 2, OptimizationStrategy.Greenest);

        result.Where(r => r.IsRecommended).Select(r => r.Co2PerKwh)
            .Should().BeEquivalentTo(new[] { 50.0, 20.0 });
    }

    [Fact]
    public void FindBestWindow_ReturnsNull_WhenEmpty()
    {
        var result = ChargingOptimizer.FindBestWindow([], 4, Now, Deadline, OptimizationStrategy.Cheapest);
        result.Should().BeNull();
    }
}
