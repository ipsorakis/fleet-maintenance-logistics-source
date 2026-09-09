using Microsoft.AspNetCore.Mvc;

namespace FML.Api.Telemetry;

[ApiController]
[Route("api/telemetry")]
public class TelemetryController : ControllerBase
{
    private readonly TelemetryService _telemetry;

    public TelemetryController(TelemetryService telemetry)
    {
        _telemetry = telemetry;
    }

    [HttpGet("readings")]
    public async Task<ActionResult<IEnumerable<TelemetryReading>>> List(
        [FromQuery] int? assetId,
        [FromQuery] string? metric,
        [FromQuery] int take = 100) =>
        Ok(await _telemetry.GetReadingsAsync(assetId, metric, take));

    [HttpPost("readings")]
    public async Task<ActionResult<IngestResult>> Ingest([FromBody] TelemetryReadingInput input)
    {
        var result = await _telemetry.IngestAsync(input);
        return result is null ? NotFound($"Unknown asset {input.AssetId}") : Ok(result);
    }

    [HttpPost("readings/batch")]
    public async Task<ActionResult<IEnumerable<IngestResult>>> IngestBatch([FromBody] List<TelemetryReadingInput> inputs) =>
        Ok(await _telemetry.IngestBatchAsync(inputs));

    [HttpPost("simulate/{assetId:int}")]
    public async Task<ActionResult<IEnumerable<IngestResult>>> Simulate(int assetId, [FromQuery] int samples = 3) =>
        Ok(await _telemetry.SimulateAsync(assetId, samples));

    [HttpGet("assets/{assetId:int}/summary")]
    public async Task<ActionResult<IEnumerable<MetricSummary>>> Summary(int assetId) =>
        Ok(await _telemetry.GetAssetSummaryAsync(assetId));

    [HttpGet("breaches")]
    public async Task<ActionResult<IEnumerable<TelemetryReading>>> Breaches([FromQuery] int take = 50) =>
        Ok(await _telemetry.GetBreachesAsync(take));

    [HttpDelete("readings/{id:int}")]
    public async Task<IActionResult> Delete(int id) =>
        await _telemetry.DeleteReadingAsync(id) ? NoContent() : NotFound();
}
