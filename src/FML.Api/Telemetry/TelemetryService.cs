using FML.Api.AssetRegistry;
using FML.Api.Common;
using FML.Api.Data;
using FML.Api.MaintenanceScheduling;
using FML.Common.Observability;
using Microsoft.EntityFrameworkCore;

namespace FML.Api.Telemetry;

public record TelemetryReadingInput(int AssetId, string Metric, double Value, string Unit, DateTime? RecordedUtc, string? Source);

public record IngestResult(TelemetryReading Reading, bool ThresholdBreached, int? WorkOrderId);

public record MetricSummary(string Metric, int Samples, double Min, double Max, double Average, DateTime LastRecordedUtc);

public class TelemetryService
{
    private readonly FmlDbContext _db;
    private readonly MaintenanceService _maintenance;
    private readonly AssetService _assets;

    public TelemetryService(FmlDbContext db, MaintenanceService maintenance, AssetService assets)
    {
        _db = db;
        _maintenance = maintenance;
        _assets = assets;
    }

    public Task<List<TelemetryReading>> GetReadingsAsync(int? assetId = null, string? metric = null, int take = 100)
    {
        var query = _db.TelemetryReadings.AsQueryable();
        if (assetId.HasValue)
        {
            query = query.Where(r => r.AssetId == assetId.Value);
        }

        if (!string.IsNullOrWhiteSpace(metric))
        {
            query = query.Where(r => r.Metric == metric);
        }

        return query.OrderByDescending(r => r.RecordedUtc).Take(take).ToListAsync();
    }

    /// <summary>
    /// Stores a reading and, when it breaches a configured threshold, immediately asks
    /// MaintenanceScheduling to raise a condition-based work order.
    /// </summary>
    public async Task<IngestResult?> IngestAsync(TelemetryReadingInput input)
    {
        using var activity = FmlTelemetry.ActivitySource.StartActivity("Telemetry.Ingest");
        var asset = await _assets.GetAsync(input.AssetId);
        if (asset is null)
        {
            return null;
        }

        var reading = new TelemetryReading
        {
            AssetId = input.AssetId,
            Metric = input.Metric,
            Value = input.Value,
            Unit = input.Unit,
            Source = input.Source ?? "api",
            RecordedUtc = input.RecordedUtc.HasValue
                ? DateTime.SpecifyKind(input.RecordedUtc.Value, DateTimeKind.Utc)
                : DateTime.UtcNow,
            ThresholdBreached = FmlConfig.IsBreach(input.Metric, input.Value),
        };
        _db.TelemetryReadings.Add(reading);
        await _db.SaveChangesAsync();

        FmlTelemetry.TelemetryReadingsIngested.Add(1);
        activity?.SetTag("fml.telemetry.metric", reading.Metric);
        activity?.SetTag("fml.telemetry.breach", reading.ThresholdBreached);

        int? workOrderId = null;
        if (reading.ThresholdBreached)
        {
            var workOrder = await _maintenance.CreateFromTelemetryAsync(reading);
            workOrderId = workOrder?.Id;
        }

        return new IngestResult(reading, reading.ThresholdBreached, workOrderId);
    }

    public async Task<List<IngestResult>> IngestBatchAsync(IEnumerable<TelemetryReadingInput> inputs)
    {
        var results = new List<IngestResult>();
        foreach (var input in inputs)
        {
            var result = await IngestAsync(input);
            if (result is not null)
            {
                results.Add(result);
            }
        }

        return results;
    }

    public async Task<List<MetricSummary>> GetAssetSummaryAsync(int assetId)
    {
        var readings = await _db.TelemetryReadings
            .Where(r => r.AssetId == assetId)
            .ToListAsync();

        return readings
            .GroupBy(r => r.Metric)
            .Select(g => new MetricSummary(
                g.Key,
                g.Count(),
                g.Min(r => r.Value),
                g.Max(r => r.Value),
                Math.Round(g.Average(r => r.Value), 2),
                g.Max(r => r.RecordedUtc)))
            .OrderBy(s => s.Metric)
            .ToList();
    }

    public Task<List<TelemetryReading>> GetBreachesAsync(int take = 50) =>
        _db.TelemetryReadings
            .Where(r => r.ThresholdBreached)
            .OrderByDescending(r => r.RecordedUtc)
            .Take(take)
            .ToListAsync();

    /// <summary>Generates a plausible reading per metric for an asset, for demos and load.</summary>
    public async Task<List<IngestResult>> SimulateAsync(int assetId, int samples = 3)
    {
        var random = new Random(assetId * 31 + samples);
        var inputs = new List<TelemetryReadingInput>();
        for (var i = 0; i < samples; i++)
        {
            inputs.Add(new TelemetryReadingInput(
                assetId,
                "EngineTempC",
                Math.Round(70 + random.NextDouble() * 20, 1),
                "C",
                DateTime.UtcNow.AddMinutes(-i * 10),
                "simulator"));
            inputs.Add(new TelemetryReadingInput(
                assetId,
                "VibrationMm",
                Math.Round(1 + random.NextDouble() * 5, 2),
                "mm/s",
                DateTime.UtcNow.AddMinutes(-i * 10),
                "simulator"));
        }

        return await IngestBatchAsync(inputs);
    }

    public async Task<bool> DeleteReadingAsync(int id)
    {
        var reading = await _db.TelemetryReadings.FirstOrDefaultAsync(r => r.Id == id);
        if (reading is null)
        {
            return false;
        }

        _db.TelemetryReadings.Remove(reading);
        await _db.SaveChangesAsync();
        return true;
    }
}
