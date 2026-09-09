using FML.Api.AssetRegistry;
using FML.Api.Common;
using FML.Api.Data;
using FML.Api.Inventory;
using FML.Api.MaintenanceScheduling;
using FML.Api.Telemetry;
using FML.Common.Observability;
using Microsoft.EntityFrameworkCore;

namespace FML.Api.Reporting;

public record FleetHealthRow(
    string AssetCode,
    string AssetName,
    AssetStatus Status,
    double OperatingHours,
    int OpenWorkOrders,
    int BreachedReadings,
    double? LatestEngineTempC);

public record FleetHealthReport(
    string FleetName,
    DateTime GeneratedUtc,
    int AssetCount,
    int AssetsUnderMaintenance,
    int OpenWorkOrders,
    int OverdueWorkOrders,
    int TelemetryBreaches,
    List<FleetHealthRow> Assets);

public record MaintenanceCostRow(string AssetCode, int CompletedWorkOrders, double LaborHours, decimal PartsCost);

public record MaintenanceCostReport(
    DateTime FromUtc,
    DateTime ToUtc,
    double TotalLaborHours,
    decimal TotalPartsCost,
    List<MaintenanceCostRow> Rows);

public record InventoryPressureRow(string Sku, string Name, int OnHand, int ReorderPoint, int OpenDemand, int Shortfall);

public record InventoryPressureReport(
    DateTime GeneratedUtc,
    decimal StockValue,
    bool StockValueAlert,
    int PartsBelowReorderPoint,
    int OpenPurchaseOrders,
    List<InventoryPressureRow> Rows);

/// <summary>
/// Cross-journey reporting. Queries every module's tables through the shared
/// DbContext and calls the other modules' services for their own logic.
/// </summary>
public class ReportService
{
    private readonly FmlDbContext _db;
    private readonly MaintenanceService _maintenance;
    private readonly InventoryService _inventory;
    private readonly TelemetryService _telemetry;

    public ReportService(
        FmlDbContext db,
        MaintenanceService maintenance,
        InventoryService inventory,
        TelemetryService telemetry)
    {
        _db = db;
        _maintenance = maintenance;
        _inventory = inventory;
        _telemetry = telemetry;
    }

    public async Task<FleetHealthReport> GenerateFleetHealthAsync()
    {
        using var activity = FmlTelemetry.ActivitySource.StartActivity("Reporting.FleetHealth");

        var assets = await _db.Assets.OrderBy(a => a.Code).ToListAsync();
        var openWorkOrders = await _db.WorkOrders
            .Where(w => w.Status != WorkOrderStatus.Completed && w.Status != WorkOrderStatus.Cancelled)
            .GroupBy(w => w.AssetId)
            .Select(g => new { AssetId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AssetId, x => x.Count);
        var breaches = await _db.TelemetryReadings
            .Where(r => r.ThresholdBreached)
            .GroupBy(r => r.AssetId)
            .Select(g => new { AssetId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AssetId, x => x.Count);
        var tempReadings = await _db.TelemetryReadings
            .Where(r => r.Metric == "EngineTempC")
            .Select(r => new { r.AssetId, r.Value, r.RecordedUtc })
            .ToListAsync();
        var latestTemps = tempReadings
            .GroupBy(r => r.AssetId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.RecordedUtc).First().Value);

        var rows = assets.Select(a => new FleetHealthRow(
            a.Code,
            a.Name,
            a.Status,
            a.OperatingHours,
            openWorkOrders.TryGetValue(a.Id, out var open) ? open : 0,
            breaches.TryGetValue(a.Id, out var breach) ? breach : 0,
            latestTemps.TryGetValue(a.Id, out var temp) ? temp : (double?)null)).ToList();

        var overdue = await _maintenance.GetOverdueWorkOrdersAsync();

        FmlTelemetry.ReportsGenerated.Add(1);
        return new FleetHealthReport(
            FmlConfig.FleetName,
            DateTime.UtcNow,
            assets.Count,
            assets.Count(a => a.Status == AssetStatus.UnderMaintenance),
            rows.Sum(r => r.OpenWorkOrders),
            overdue.Count,
            rows.Sum(r => r.BreachedReadings),
            rows);
    }

    public async Task<MaintenanceCostReport> GenerateMaintenanceCostAsync(DateTime? fromUtc = null, DateTime? toUtc = null)
    {
        using var activity = FmlTelemetry.ActivitySource.StartActivity("Reporting.MaintenanceCost");

        var from = fromUtc.HasValue ? DateTime.SpecifyKind(fromUtc.Value, DateTimeKind.Utc) : DateTime.UtcNow.AddYears(-2);
        var to = toUtc.HasValue ? DateTime.SpecifyKind(toUtc.Value, DateTimeKind.Utc) : DateTime.UtcNow.AddDays(1);

        var completed = await _db.WorkOrders
            .Include(w => w.Asset)
            .Include(w => w.Parts)
            .ThenInclude(p => p.Part)
            .Where(w => w.Status == WorkOrderStatus.Completed
                        && w.CompletedUtc != null
                        && w.CompletedUtc >= from
                        && w.CompletedUtc <= to)
            .ToListAsync();

        var rows = completed
            .GroupBy(w => w.Asset!.Code)
            .Select(g => new MaintenanceCostRow(
                g.Key,
                g.Count(),
                g.Sum(w => w.LaborHours),
                g.Sum(w => w.Parts.Sum(p => (p.Part?.UnitCost ?? 0m) * p.QuantityIssued))))
            .OrderBy(r => r.AssetCode)
            .ToList();

        FmlTelemetry.ReportsGenerated.Add(1);
        return new MaintenanceCostReport(
            from,
            to,
            rows.Sum(r => r.LaborHours),
            rows.Sum(r => r.PartsCost),
            rows);
    }

    public async Task<InventoryPressureReport> GenerateInventoryPressureAsync()
    {
        using var activity = FmlTelemetry.ActivitySource.StartActivity("Reporting.InventoryPressure");

        var demands = await _inventory.GetPartsBelowReorderPointAsync();
        var stockValue = await _inventory.GetStockValueAsync();
        var openPurchaseOrders = await _db.PurchaseOrders
            .CountAsync(p => p.Status == PurchaseOrderStatus.Placed || p.Status == PurchaseOrderStatus.Draft);

        var parts = await _db.Parts.ToDictionaryAsync(p => p.Id, p => p);
        var rows = demands
            .Select(d => new InventoryPressureRow(
                d.Sku,
                d.Name,
                d.OnHand,
                parts[d.PartId].ReorderPoint,
                d.OpenWorkOrderDemand,
                d.Shortfall))
            .ToList();

        FmlTelemetry.ReportsGenerated.Add(1);
        return new InventoryPressureReport(
            DateTime.UtcNow,
            stockValue,
            stockValue >= FmlConfig.StockValueAlertThreshold,
            rows.Count,
            openPurchaseOrders,
            rows);
    }

    /// <summary>Per-asset drill-down combining registry, telemetry, maintenance and parts data.</summary>
    public async Task<object?> GenerateAssetDossierAsync(int assetId)
    {
        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Id == assetId);
        if (asset is null)
        {
            return null;
        }

        var metrics = await _telemetry.GetAssetSummaryAsync(assetId);
        var workOrders = await _maintenance.GetWorkOrdersAsync(assetId: assetId);
        var partIds = workOrders.SelectMany(w => w.Parts).Select(p => p.PartId).Distinct().ToList();
        var parts = await _db.Parts.Where(p => partIds.Contains(p.Id)).ToListAsync();

        FmlTelemetry.ReportsGenerated.Add(1);
        return new
        {
            Asset = new { asset.Id, asset.Code, asset.Name, asset.Status, asset.OperatingHours, asset.HomePort },
            Metrics = metrics,
            WorkOrders = workOrders.Select(w => new { w.Id, w.Title, w.Status, w.Priority, w.Trigger, w.ScheduledForUtc, w.CompletedUtc }),
            Parts = parts.Select(p => new { p.Id, p.Sku, p.Name, p.QuantityOnHand, p.ReorderPoint }),
        };
    }
}
