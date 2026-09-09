using Microsoft.AspNetCore.Mvc;

namespace FML.Api.Inventory;

[ApiController]
[Route("api/inventory")]
public class InventoryController : ControllerBase
{
    private readonly InventoryService _inventory;

    public InventoryController(InventoryService inventory)
    {
        _inventory = inventory;
    }

    [HttpGet("parts")]
    public async Task<ActionResult<IEnumerable<Part>>> ListParts([FromQuery] string? category) =>
        Ok(await _inventory.GetPartsAsync(category));

    [HttpGet("parts/{id:int}")]
    public async Task<ActionResult<Part>> GetPart(int id)
    {
        var part = await _inventory.GetPartAsync(id);
        return part is null ? NotFound() : Ok(part);
    }

    [HttpPost("parts")]
    public async Task<ActionResult<Part>> CreatePart([FromBody] PartInput input)
    {
        var part = await _inventory.CreatePartAsync(input);
        return CreatedAtAction(nameof(GetPart), new { id = part.Id }, part);
    }

    [HttpPut("parts/{id:int}")]
    public async Task<ActionResult<Part>> UpdatePart(int id, [FromBody] PartInput input)
    {
        var part = await _inventory.UpdatePartAsync(id, input);
        return part is null ? NotFound() : Ok(part);
    }

    [HttpDelete("parts/{id:int}")]
    public async Task<IActionResult> DeletePart(int id) =>
        await _inventory.DeletePartAsync(id) ? NoContent() : NotFound();

    [HttpGet("reorder-candidates")]
    public async Task<ActionResult<IEnumerable<PartDemand>>> ReorderCandidates() =>
        Ok(await _inventory.GetPartsBelowReorderPointAsync());

    [HttpPost("parts/{id:int}/reorder")]
    public async Task<ActionResult<ReorderResult>> Reorder(int id, [FromQuery] int? quantity)
    {
        var result = await _inventory.ReorderPartAsync(id, quantity);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("reorder-run")]
    public async Task<ActionResult<IEnumerable<ReorderResult>>> ReorderRun() =>
        Ok(await _inventory.ReorderAllBelowReorderPointAsync());

    [HttpGet("purchase-orders")]
    public async Task<ActionResult<IEnumerable<PurchaseOrder>>> PurchaseOrders([FromQuery] PurchaseOrderStatus? status) =>
        Ok(await _inventory.GetPurchaseOrdersAsync(status));

    [HttpPost("purchase-orders/{id:int}/receive")]
    public async Task<ActionResult<PurchaseOrder>> Receive(int id)
    {
        var order = await _inventory.ReceivePurchaseOrderAsync(id);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpGet("movements")]
    public async Task<ActionResult<IEnumerable<StockMovement>>> Movements([FromQuery] int? partId) =>
        Ok(await _inventory.GetMovementsAsync(partId));

    [HttpGet("stock-value")]
    public async Task<ActionResult<decimal>> StockValue() =>
        Ok(await _inventory.GetStockValueAsync());
}
