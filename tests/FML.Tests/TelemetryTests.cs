using FML.Api.MaintenanceScheduling;
using FML.Api.Telemetry;

namespace FML.Tests;

public class TelemetryTests
{
    [Fact]
    public async Task Ingest_stores_reading_within_thresholds_without_work_order()
    {
        using var ctx = new FmlTestContext();
        var asset = await ctx.CreateAssetAsync();

        var result = await ctx.Telemetry.IngestAsync(
            new TelemetryReadingInput(asset.Id, "EngineTempC", 78.4, "C", null, null));

        Assert.NotNull(result);
        Assert.False(result!.ThresholdBreached);
        Assert.Null(result.WorkOrderId);
        Assert.Equal("api", result.Reading.Source);
        Assert.Single(await ctx.Telemetry.GetReadingsAsync(asset.Id));
    }

    [Fact]
    public async Task Ingest_for_unknown_asset_is_rejected()
    {
        using var ctx = new FmlTestContext();

        var result = await ctx.Telemetry.IngestAsync(
            new TelemetryReadingInput(4242, "EngineTempC", 80, "C", null, null));

        Assert.Null(result);
        Assert.Empty(await ctx.Telemetry.GetReadingsAsync());
    }

    [Theory]
    [InlineData("EngineTempC", 99.5)]
    [InlineData("VibrationMm", 8.2)]
    [InlineData("OilPressureBar", 1.4)]
    public async Task Breaching_reading_raises_a_condition_based_work_order(string metric, double value)
    {
        using var ctx = new FmlTestContext();
        await ctx.CreateTechnicianAsync();
        var asset = await ctx.CreateAssetAsync();

        var result = await ctx.Telemetry.IngestAsync(
            new TelemetryReadingInput(asset.Id, metric, value, "u", null, "sensor"));

        Assert.NotNull(result);
        Assert.True(result!.ThresholdBreached);
        Assert.NotNull(result.WorkOrderId);

        var workOrder = await ctx.Maintenance.GetWorkOrderAsync(result.WorkOrderId!.Value);
        Assert.NotNull(workOrder);
        Assert.Equal(MaintenanceTrigger.ConditionBased, workOrder!.Trigger);
        Assert.Equal(WorkOrderPriority.Critical, workOrder.Priority);
        Assert.Equal(result.Reading.Id, workOrder.TriggeringReadingId);
        Assert.NotNull(workOrder.AssignedToUserId);
    }

    [Fact]
    public async Task Repeated_breaches_reuse_the_open_work_order()
    {
        using var ctx = new FmlTestContext();
        var asset = await ctx.CreateAssetAsync();

        var first = await ctx.Telemetry.IngestAsync(
            new TelemetryReadingInput(asset.Id, "EngineTempC", 96, "C", null, null));
        var second = await ctx.Telemetry.IngestAsync(
            new TelemetryReadingInput(asset.Id, "EngineTempC", 101, "C", null, null));

        Assert.Equal(first!.WorkOrderId, second!.WorkOrderId);
        Assert.Single(await ctx.Maintenance.GetWorkOrdersAsync(assetId: asset.Id));
        Assert.Equal(2, (await ctx.Telemetry.GetBreachesAsync()).Count);
    }

    [Fact]
    public async Task Summary_aggregates_per_metric_and_simulation_fills_history()
    {
        using var ctx = new FmlTestContext();
        var asset = await ctx.CreateAssetAsync();

        var simulated = await ctx.Telemetry.SimulateAsync(asset.Id, samples: 3);
        var summary = await ctx.Telemetry.GetAssetSummaryAsync(asset.Id);

        Assert.Equal(6, simulated.Count);
        Assert.Equal(new[] { "EngineTempC", "VibrationMm" }, summary.Select(s => s.Metric));
        Assert.All(summary, s => Assert.Equal(3, s.Samples));
        Assert.All(summary, s => Assert.InRange(s.Average, s.Min, s.Max));
    }

    [Fact]
    public async Task Reading_can_be_deleted()
    {
        using var ctx = new FmlTestContext();
        var asset = await ctx.CreateAssetAsync();
        var result = await ctx.Telemetry.IngestAsync(
            new TelemetryReadingInput(asset.Id, "EngineTempC", 70, "C", null, null));

        Assert.True(await ctx.Telemetry.DeleteReadingAsync(result!.Reading.Id));
        Assert.False(await ctx.Telemetry.DeleteReadingAsync(result.Reading.Id));
    }
}
