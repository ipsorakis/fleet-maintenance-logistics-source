using FML.Api.AssetRegistry;
using FML.Api.Auth;
using FML.Api.Inventory;
using FML.Common.Auth;

namespace FML.Api.MaintenanceScheduling;

public enum WorkOrderStatus
{
    Open = 0,
    Scheduled = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4,
}

public enum WorkOrderPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3,
}

public enum MaintenanceTrigger
{
    Manual = 0,
    Preventive = 1,
    ConditionBased = 2,
    Corrective = 3,
}

public class WorkOrder
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public Asset? Asset { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public WorkOrderStatus Status { get; set; } = WorkOrderStatus.Open;
    public WorkOrderPriority Priority { get; set; } = WorkOrderPriority.Normal;
    public MaintenanceTrigger Trigger { get; set; } = MaintenanceTrigger.Manual;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ScheduledForUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public double LaborHours { get; set; }

    public int? TriggeringReadingId { get; set; }
    public int? AssignedToUserId { get; set; }
    public User? AssignedTo { get; set; }

    public List<WorkOrderPart> Parts { get; set; } = new();
}

/// <summary>Parts a work order needs. Maintenance writes it; Inventory reads it to compute demand.</summary>
public class WorkOrderPart
{
    public int Id { get; set; }
    public int WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    public int PartId { get; set; }
    public Part? Part { get; set; }

    public int QuantityRequired { get; set; }
    public int QuantityIssued { get; set; }
}
