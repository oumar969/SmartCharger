using FluentAssertions;
using SmartCharger.Domain.Logic;
using SmartCharger.Domain.Models;

namespace SmartCharger.Tests;

public class ChargingOptimizerTests
{
    private static readonly DateTime Now      = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Deadline = new(2026, 1, 2,  7, 0, 0, DateTimeKind.Utc);

    private static List<HourData> BuildHours(double[] prices, double[]? co2 = null) =>
        prices.Select((p, i) => new HourData(Now.AddHours(i), p, co2?[i] ?? 100.0, "DK2")).ToList();

    [Fact]
    public void FindBestWindow_ReturnsCheapestContiguousBlock()
    {
        var result = ChargingOptimizer.FindBestWindow(
            BuildHours([5, 4, 3, 1, 2, 6, 7, 8]), 3, Now, Deadline, OptimizationStrategy.Cheapest);
        result.Should().NotBeNull();
        result!.WindowStart.Should().Be(Now.AddHours(2));
        result.TotalCostDKK.Should().Be(6);
    }

    [Fact]
    public void FindBestWindow_ReturnsGreenestContiguousBlock()
    {
        var result = ChargingOptimizer.FindBestWindow(
            BuildHours([5, 4, 3, 1, 2, 6, 7, 8], [90, 80, 20, 10, 15, 200, 300, 400]),
            3, Now, Deadline, OptimizationStrategy.Greenest);
        result.Should().NotBeNull();
        result!.WindowStart.Should().Be(Now.AddHours(2));
    }

    [Fact]
    public void FindBestWindow_ReturnsNull_WhenNotEnoughHours()
    {
        var result = ChargingOptimizer.FindBestWindow(
            BuildHours([1, 2]), 5, Now, Deadline, OptimizationStrategy.Cheapest);
        result.Should().BeNull();
    }

    [Fact]
    public void FindBestWindow_IgnoresHoursPastDeadline()
    {
        var result = ChargingOptimizer.FindBestWindow(
            BuildHours([9, 9, 9, 1, 1, 1, 1]), 3, Now, Now.AddHours(4), OptimizationStrategy.Cheapest);
        result.Should().NotBeNull();
        result!.WindowStart.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void FindBestWindow_HandlesNegativePrices()
    {
        var result = ChargingOptimizer.FindBestWindow(
            BuildHours([-2, -3, -1, 5, 6, 7]), 2, Now, Deadline, OptimizationStrategy.Cheapest);
        result.Should().NotBeNull();
        result!.TotalCostDKK.Should().Be(-5);
    }

    [Fact]
    public void MarkRecommended_MarksCorrectHours_Cheapest()
    {
        var result = ChargingOptimizer.MarkRecommended(BuildHours([5, 1, 3, 2, 4]), 2, OptimizationStrategy.Cheapest);
        result.Where(r => r.IsRecommended).Select(r => r.PriceDKK)
            .Should().BeEquivalentTo([1.0, 2.0]);
    }

    [Fact]
    public void MarkRecommended_MarksCorrectHours_Greenest()
    {
        var result = ChargingOptimizer.MarkRecommended(
            BuildHours([1, 1, 1, 1, 1], [300, 50, 100, 20, 200]), 2, OptimizationStrategy.Greenest);
        result.Where(r => r.IsRecommended).Select(r => r.Co2PerKwh)
            .Should().BeEquivalentTo([50.0, 20.0]);
    }

    [Fact]
    public void FindBestWindow_ReturnsNull_WhenEmpty()
    {
        ChargingOptimizer.FindBestWindow([], 4, Now, Deadline, OptimizationStrategy.Cheapest)
            .Should().BeNull();
    }
}
