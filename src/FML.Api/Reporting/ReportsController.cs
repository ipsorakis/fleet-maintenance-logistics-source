using Microsoft.AspNetCore.Mvc;

namespace FML.Api.Reporting;

[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly ReportService _reports;

    public ReportsController(ReportService reports)
    {
        _reports = reports;
    }

    [HttpGet("fleet-health")]
    public async Task<ActionResult<FleetHealthReport>> FleetHealth() =>
        Ok(await _reports.GenerateFleetHealthAsync());

    [HttpGet("maintenance-cost")]
    public async Task<ActionResult<MaintenanceCostReport>> MaintenanceCost(
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc) =>
        Ok(await _reports.GenerateMaintenanceCostAsync(fromUtc, toUtc));

    [HttpGet("inventory-pressure")]
    public async Task<ActionResult<InventoryPressureReport>> InventoryPressure() =>
        Ok(await _reports.GenerateInventoryPressureAsync());

    [HttpGet("assets/{assetId:int}/dossier")]
    public async Task<IActionResult> AssetDossier(int assetId)
    {
        var dossier = await _reports.GenerateAssetDossierAsync(assetId);
        return dossier is null ? NotFound() : Ok(dossier);
    }
}
