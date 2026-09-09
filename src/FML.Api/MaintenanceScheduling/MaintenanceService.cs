using FML.Api.AssetRegistry;
using FML.Api.Auth;
using FML.Api.Common;
using FML.Api.Data;
using FML.Api.Inventory;
using FML.Api.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace FML.Api.MaintenanceScheduling;

public record WorkOrderPartInput(int PartId, int QuantityRequired);

public record WorkOrderInput(
    int AssetId,
    string Title,
    string Description,
    WorkOrderPriority Priority,
    MaintenanceTrigger Trigger,
    DateTime? ScheduledForUtc,
    int? AssignedToUserId,
    List<WorkOrderPartInput>? Parts);

public record MaintenanceCompletionResult(WorkOrder WorkOrder, int PartLinesIssued, int PartsReordered);

public class MaintenanceService
{
    private readonly FmlDbContext _db;
    private readonly AssetService _assets;
    private readonly InventoryService _inventory;
    private readonly AuthService _auth;

    public MaintenanceService(FmlDbContext db, AssetService assets, InventoryService inventory, AuthService auth)
    {
        _db = db;
        _assets = assets;
        _inventory = inventory;
        _auth = auth;
    }

    public Task<List<WorkOrder>> GetWorkOrdersAsync(WorkOrderStatus? status = null, int? assetId = null)
    {
        var query = _db.WorkOrders.Include(w => w.Parts).AsQueryable();
        if (status.HasValue)
        {
            query = query.Where(w => w.Status == status.Value);
        }

        if (assetId.HasValue)
        {
            query = query.Where(w => w.AssetId == assetId.Value);
        }

        return query.OrderByDescending(w => w.CreatedUtc).ToListAsync();
    }

    public Task<WorkOrder?> GetWorkOrderAsync(int id) =>
        _db.WorkOrders.Include(w => w.Parts).FirstOrDefaultAsync(w => w.Id == id);

    public async Task<WorkOrder?> CreateWorkOrderAsync(WorkOrderInput input)
    {
        using var activity = FmlTelemetry.ActivitySource.StartActivity("Maintenance.CreateWorkOrder");
        var asset = await _assets.GetAsync(input.AssetId);
        if (asset is null)
        {
            return null;
        }

        var assignee = input.AssignedToUserId;
        if (assignee is null)
        {
            var technician = await _auth.FindAvailableTechnicianAsync();
            assignee = technician?.Id;
        }

        var workOrder = new WorkOrder
        {
            AssetId = asset.Id,
            Title = input.Title,
            Description = input.Description,
            Priority = input.Priority,
            Trigger = input.Trigger,
            Status = input.ScheduledForUtc.HasValue ? WorkOrderStatus.Scheduled : WorkOrderStatus.Open,
            ScheduledForUtc = input.ScheduledForUtc.HasValue
                ? DateTime.SpecifyKind(input.ScheduledForUtc.Value, DateTimeKind.Utc)
                : DateTime.UtcNow.AddDays(FmlConfig.WorkOrderDueInDays),
            AssignedToUserId = assignee,
        };

        foreach (var line in input.Parts ?? new List<WorkOrderPartInput>())
        {
            workOrder.Parts.Add(new WorkOrderPart { PartId = line.PartId, QuantityRequired = line.QuantityRequired });
        }

        _db.WorkOrders.Add(workOrder);
        await _db.SaveChangesAsync();

        FmlTelemetry.WorkOrdersCreated.Add(1);
        activity?.SetTag("fml.workorder.id", workOrder.Id);
        return workOrder;
    }

    public async Task<WorkOrder?> UpdateWorkOrderAsync(int id, WorkOrderInput input)
    {
        var workOrder = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (workOrder is null)
        {
            return null;
        }

        workOrder.Title = input.Title;
        workOrder.Description = input.Description;
        workOrder.Priority = input.Priority;
        workOrder.Trigger = input.Trigger;
        workOrder.AssignedToUserId = input.AssignedToUserId ?? workOrder.AssignedToUserId;
        if (input.ScheduledForUtc.HasValue)
        {
            workOrder.ScheduledForUtc = DateTime.SpecifyKind(input.ScheduledForUtc.Value, DateTimeKind.Utc);
        }

        await _db.SaveChangesAsync();
        return workOrder;
    }

    public async Task<bool> CancelWorkOrderAsync(int id)
    {
        var workOrder = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (workOrder is null)
        {
            return false;
        }

        workOrder.Status = WorkOrderStatus.Cancelled;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<WorkOrder?> StartWorkOrderAsync(int id)
    {
        var workOrder = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (workOrder is null)
        {
            return null;
        }

        workOrder.Status = WorkOrderStatus.InProgress;
        await _db.SaveChangesAsync();
        await _assets.SetStatusAsync(workOrder.AssetId, AssetStatus.UnderMaintenance);
        return workOrder;
    }

    /// <summary>
    /// Completes a work order: issues its parts from Inventory, tops the stock back
    /// up when a part drops below its reorder point and puts the asset back in service.
    /// </summary>
    public async Task<MaintenanceCompletionResult?> CompleteWorkOrderAsync(int id, double laborHours)
    {
        using var activity = FmlTelemetry.ActivitySource.StartActivity("Maintenance.CompleteWorkOrder");
        var workOrder = await _db.WorkOrders.Include(w => w.Parts).FirstOrDefaultAsync(w => w.Id == id);
        if (workOrder is null)
        {
            return null;
        }

        var movements = await _inventory.IssuePartsToWorkOrderAsync(workOrder.Id);

        workOrder.Status = WorkOrderStatus.Completed;
        workOrder.CompletedUtc = DateTime.UtcNow;
        workOrder.LaborHours = laborHours;
        await _db.SaveChangesAsync();

        await _assets.SetStatusAsync(workOrder.AssetId, AssetStatus.InService);

        var reordered = 0;
        foreach (var movement in movements)
        {
            var part = await _inventory.GetPartAsync(movement.PartId);
            if (part is not null && part.QuantityOnHand <= part.ReorderPoint)
            {
                var result = await _inventory.ReorderPartAsync(part.Id);
                if (result is not null)
                {
                    reordered++;
                }
            }
        }

        activity?.SetTag("fml.workorder.parts_issued", movements.Count);
        return new MaintenanceCompletionResult(workOrder, movements.Count, reordered);
    }

    /// <summary>
    /// Raises a condition-based work order from a telemetry reading that breached a
    /// threshold. Called synchronously by TelemetryService during ingestion.
    /// </summary>
    public async Task<WorkOrder?> CreateFromTelemetryAsync(TelemetryReading reading)
    {
        var existing = await _db.WorkOrders.FirstOrDefaultAsync(w =>
            w.AssetId == reading.AssetId
            && w.Trigger == MaintenanceTrigger.ConditionBased
            && (w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.Scheduled || w.Status == WorkOrderStatus.InProgress));
        if (existing is not null)
        {
            return existing;
        }

        var asset = await _assets.GetAsync(reading.AssetId);
        if (asset is null)
        {
            return null;
        }

        var technician = await _auth.FindAvailableTechnicianAsync();
        var workOrder = new WorkOrder
        {
            AssetId = reading.AssetId,
            Title = $"Condition alert: {reading.Metric} on {asset.Code}",
            Description = $"{reading.Metric} reached {reading.Value} {reading.Unit} at {reading.RecordedUtc:u}, " +
                          "beyond the configured threshold. Inspect the asset.",
            Status = WorkOrderStatus.Open,
            Priority = WorkOrderPriority.Critical,
            Trigger = MaintenanceTrigger.ConditionBased,
            ScheduledForUtc = DateTime.UtcNow.AddDays(1),
            TriggeringReadingId = reading.Id,
            AssignedToUserId = technician?.Id,
        };
        _db.WorkOrders.Add(workOrder);
        await _db.SaveChangesAsync();

        FmlTelemetry.WorkOrdersCreated.Add(1);
        return workOrder;
    }

    /// <summary>Creates preventive work orders for every asset past its service interval.</summary>
    public async Task<List<WorkOrder>> RunPreventiveMaintenanceSweepAsync()
    {
        var created = new List<WorkOrder>();
        foreach (var asset in await _assets.GetAssetsDueForPreventiveMaintenanceAsync())
        {
            var alreadyPlanned = await _db.WorkOrders.AnyAsync(w =>
                w.AssetId == asset.Id
                && w.Trigger == MaintenanceTrigger.Preventive
                && (w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.Scheduled));
            if (alreadyPlanned)
            {
                continue;
            }

            var workOrder = new WorkOrder
            {
                AssetId = asset.Id,
                Title = $"{FmlConfig.PreventiveMaintenanceIntervalHours}h preventive service - {asset.Code}",
                Description = $"Asset has {asset.OperatingHours:F0} operating hours logged.",
                Status = WorkOrderStatus.Scheduled,
                Priority = WorkOrderPriority.Normal,
                Trigger = MaintenanceTrigger.Preventive,
                ScheduledForUtc = DateTime.UtcNow.AddDays(FmlConfig.WorkOrderDueInDays),
            };
            _db.WorkOrders.Add(workOrder);
            created.Add(workOrder);
        }

        await _db.SaveChangesAsync();
        FmlTelemetry.WorkOrdersCreated.Add(created.Count);
        return created;
    }

    public async Task<WorkOrderPart?> AddPartToWorkOrderAsync(int workOrderId, WorkOrderPartInput input)
    {
        var workOrder = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == workOrderId);
        var part = await _inventory.GetPartAsync(input.PartId);
        if (workOrder is null || part is null)
        {
            return null;
        }

        var line = await _db.WorkOrderParts
            .FirstOrDefaultAsync(wp => wp.WorkOrderId == workOrderId && wp.PartId == input.PartId);
        if (line is null)
        {
            line = new WorkOrderPart { WorkOrderId = workOrderId, PartId = input.PartId };
            _db.WorkOrderParts.Add(line);
        }

        line.QuantityRequired += input.QuantityRequired;
        await _db.SaveChangesAsync();
        return line;
    }

    public Task<List<WorkOrder>> GetOverdueWorkOrdersAsync()
    {
        var now = DateTime.UtcNow;
        return _db.WorkOrders
            .Where(w => w.ScheduledForUtc != null
                        && w.ScheduledForUtc < now
                        && w.Status != WorkOrderStatus.Completed
                        && w.Status != WorkOrderStatus.Cancelled)
            .OrderBy(w => w.ScheduledForUtc)
            .ToListAsync();
    }
}
