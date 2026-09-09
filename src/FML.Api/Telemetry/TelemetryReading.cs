using FML.Api.AssetRegistry;

namespace FML.Api.Telemetry;

public class TelemetryReading
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public Asset? Asset { get; set; }

    public string Metric { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Source { get; set; } = "sensor";
    public DateTime RecordedUtc { get; set; } = DateTime.UtcNow;
    public bool ThresholdBreached { get; set; }
}
