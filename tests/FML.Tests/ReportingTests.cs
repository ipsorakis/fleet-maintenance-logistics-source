using FML.Api.AssetRegistry;
using FML.Api.Common;
using FML.Api.MaintenanceScheduling;
using FML.Api.Telemetry;

namespace FML.Tests;

public class ReportingTests
{
    [Fact]
    public async Task Fleet_health_summarises_the_seeded_fleet()
    {
        using var ctx = new FmlTestContext(seed: true);

        var report = await ctx.Reports.GenerateFleetHealthAsync();

        Assert.Equal(FmlConfig.FleetName, report.FleetName);
        Assert.Equal(10, report.AssetCount);
        Assert.Equal(10, report.Assets.Count);
        Assert.True(report.AssetsUnderMaintenance >= 1);
        Assert.True(report.OpenWorkOrders >= 1);
        Assert.True(report.TelemetryBreaches >= 1);
        Assert.Equal(report.Assets.Sum(a => a.OpenWorkOrders), report.OpenWorkOrders);
        Assert.Contains(report.Assets, a => a.LatestEngineTempC.HasValue);
    }

    [Fact]
    public async Task Fleet_health_reflects_a_freshly_ingested_breach()
    {
        using var ctx = new FmlTestContext();
        var asset = await ctx.CreateAssetAsync("ENG-920");

        var before = await ctx.Reports.GenerateFleetHealthAsync();
        await ctx.Telemetry.IngestAsync(new TelemetryReadingInput(asset.Id, "EngineTempC", 104, "C", null, null));
        var after = await ctx.Reports.GenerateFleetHealthAsync();

        Assert.Equal(0, before.TelemetryBreaches);
        Assert.Equal(0, before.OpenWorkOrders);
        Assert.Equal(1, after.TelemetryBreaches);
        Assert.Equal(1, after.OpenWorkOrders);
        Assert.Equal(104, Assert.Single(after.Assets).LatestEngineTempC);
    }

    [Fact]
    public async Task Maintenance_cost_only_counts_completed_work_in_the_window()
    {
        using var ctx = new FmlTestContext();
        var asset = await ctx.CreateAssetAsync("ENG-921");
        var part = await ctx.CreatePartAsync("PRT-200", onHand: 10, reorderPoint: 1);
        var completed = await ctx.Maintenance.CreateWorkOrderAsync(new WorkOrderInput(
            asset.Id,
            "Overhaul",
            "",
            WorkOrderPriority.High,
            MaintenanceTrigger.Corrective,
            null,
            null,
            new List<WorkOrderPartInput> { new(part.Id, 2) }));
        await ctx.Maintenance.CreateWorkOrderAsync(new WorkOrderInput(
            asset.Id, "Still open", "", WorkOrderPriority.Low, MaintenanceTrigger.Manual, null, null, null));
        await ctx.Maintenance.CompleteWorkOrderAsync(completed!.Id, laborHours: 4);

        var report = await ctx.Reports.GenerateMaintenanceCostAsync();
        var empty = await ctx.Reports.GenerateMaintenanceCostAsync(
            DateTime.UtcNow.AddYears(-2), DateTime.UtcNow.AddYears(-1));

        var row = Assert.Single(report.Rows);
        Assert.Equal("ENG-921", row.AssetCode);
        Assert.Equal(1, row.CompletedWorkOrders);
        Assert.Equal(4, report.TotalLaborHours);
        Assert.Equal(part.UnitCost * 2, report.TotalPartsCost);
        Assert.Empty(empty.Rows);
    }

    [Fact]
    public async Task Inventory_pressure_reuses_inventory_logic_and_flags_stock_value()
    {
        using var ctx = new FmlTestContext();
        await ctx.CreatePartAsync("PRT-201", onHand: 1, reorderPoint: 5, reorderQuantity: 10);
        await ctx.CreatePartAsync("PRT-202", onHand: 40, reorderPoint: 5, reorderQuantity: 10);
        await ctx.Inventory.ReorderAllBelowReorderPointAsync();

        var report = await ctx.Reports.GenerateInventoryPressureAsync();

        Assert.Equal(1, report.PartsBelowReorderPoint);
        Assert.Equal("PRT-201", Assert.Single(report.Rows).Sku);
        Assert.Equal(5, report.Rows[0].ReorderPoint);
        Assert.Equal(1, report.OpenPurchaseOrders);
        Assert.Equal(await ctx.Inventory.GetStockValueAsync(), report.StockValue);
        Assert.Equal(report.StockValue >= FmlConfig.StockValueAlertThreshold, report.StockValueAlert);
    }

    [Fact]
    public async Task Asset_dossier_joins_every_module_and_handles_unknown_assets()
    {
        using var ctx = new FmlTestContext(seed: true);
        var asset = await ctx.Assets.GetByCodeAsync("ENG-101");

        var dossier = await ctx.Reports.GenerateAssetDossierAsync(asset!.Id);

        Assert.NotNull(dossier);
        Assert.Null(await ctx.Reports.GenerateAssetDossierAsync(9999));

        var metrics = await ctx.Telemetry.GetAssetSummaryAsync(asset.Id);
        var workOrders = await ctx.Maintenance.GetWorkOrdersAsync(assetId: asset.Id);
        Assert.NotEmpty(metrics);
        Assert.NotEmpty(workOrders);
    }

    [Fact]
    public void Seeded_fleet_has_the_expected_baseline_shape()
    {
        using var ctx = new FmlTestContext(seed: true);

        Assert.Equal(5, ctx.Db.Users.Count());
        Assert.Equal(10, ctx.Db.Assets.Count());
        Assert.InRange(ctx.Db.TelemetryReadings.Count(), 20, 30);
        Assert.True(ctx.Db.WorkOrders.Count() >= 4);
        Assert.True(ctx.Db.Parts.Count() >= 6);
        Assert.Contains(ctx.Db.Assets, a => a.Status == AssetStatus.UnderMaintenance);
        Assert.Contains(ctx.Db.TelemetryReadings, r => r.ThresholdBreached);
    }
}
