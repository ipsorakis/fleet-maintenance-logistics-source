using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FML.Api.Common;

/// <summary>OpenTelemetry activity source and meter shared by all modules.</summary>
public static class FmlTelemetry
{
    public const string ServiceName = "fml-monolith";

    public static readonly ActivitySource ActivitySource = new(ServiceName);
    public static readonly Meter Meter = new(ServiceName);

    public static readonly Counter<long> TelemetryReadingsIngested =
        Meter.CreateCounter<long>("fml.telemetry.readings_ingested");

    public static readonly Counter<long> WorkOrdersCreated =
        Meter.CreateCounter<long>("fml.maintenance.work_orders_created");

    public static readonly Counter<long> PartsReordered =
        Meter.CreateCounter<long>("fml.inventory.parts_reordered");

    public static readonly Counter<long> ReportsGenerated =
        Meter.CreateCounter<long>("fml.reporting.reports_generated");
}
