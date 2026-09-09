using FML.Api.MaintenanceScheduling;
using FML.Api.Telemetry;

namespace FML.Api.AssetRegistry;

public enum AssetType
{
    Vessel = 0,
    Engine = 1,
    Crane = 2,
    Pump = 3,
    Generator = 4,
}

public enum AssetStatus
{
    InService = 0,
    UnderMaintenance = 1,
    Standby = 2,
    Retired = 3,
}

public class Asset
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AssetType AssetType { get; set; }
    public AssetStatus Status { get; set; }
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string HomePort { get; set; } = string.Empty;
    public DateTime CommissionedOn { get; set; }
    public double OperatingHours { get; set; }
    public double CapacityTonnes { get; set; }
    public double RatedPowerKw { get; set; }
    public DateTime? LastServicedUtc { get; set; }

    public List<TelemetryReading> Readings { get; set; } = new();
    public List<WorkOrder> WorkOrders { get; set; } = new();
}
