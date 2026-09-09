using Microsoft.AspNetCore.Mvc;

namespace FML.Api.AssetRegistry;

[ApiController]
[Route("api/assets")]
public class AssetsController : ControllerBase
{
    private readonly AssetService _assets;

    public AssetsController(AssetService assets)
    {
        _assets = assets;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Asset>>> List([FromQuery] AssetStatus? status, [FromQuery] AssetType? type) =>
        Ok(await _assets.GetAllAsync(status, type));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Asset>> Get(int id)
    {
        var asset = await _assets.GetAsync(id);
        return asset is null ? NotFound() : Ok(asset);
    }

    [HttpGet("by-code/{code}")]
    public async Task<ActionResult<Asset>> GetByCode(string code)
    {
        var asset = await _assets.GetByCodeAsync(code);
        return asset is null ? NotFound() : Ok(asset);
    }

    [HttpPost]
    public async Task<ActionResult<Asset>> Create([FromBody] AssetInput input)
    {
        var asset = await _assets.CreateAsync(input);
        return CreatedAtAction(nameof(Get), new { id = asset.Id }, asset);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<Asset>> Update(int id, [FromBody] AssetInput input)
    {
        var asset = await _assets.UpdateAsync(id, input);
        return asset is null ? NotFound() : Ok(asset);
    }

    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromQuery] AssetStatus status)
    {
        if (await _assets.GetAsync(id) is null)
        {
            return NotFound();
        }

        await _assets.SetStatusAsync(id, status);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id) =>
        await _assets.DeleteAsync(id) ? NoContent() : NotFound();

    [HttpGet("due-for-maintenance")]
    public async Task<ActionResult<IEnumerable<Asset>>> DueForMaintenance() =>
        Ok(await _assets.GetAssetsDueForPreventiveMaintenanceAsync());
}
