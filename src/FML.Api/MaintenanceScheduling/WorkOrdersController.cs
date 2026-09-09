using Microsoft.AspNetCore.Mvc;

namespace FML.Api.MaintenanceScheduling;

[ApiController]
[Route("api/work-orders")]
public class WorkOrdersController : ControllerBase
{
    private readonly MaintenanceService _maintenance;

    public WorkOrdersController(MaintenanceService maintenance)
    {
        _maintenance = maintenance;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<WorkOrder>>> List([FromQuery] WorkOrderStatus? status, [FromQuery] int? assetId) =>
        Ok(await _maintenance.GetWorkOrdersAsync(status, assetId));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<WorkOrder>> Get(int id)
    {
        var workOrder = await _maintenance.GetWorkOrderAsync(id);
        return workOrder is null ? NotFound() : Ok(workOrder);
    }

    [HttpPost]
    public async Task<ActionResult<WorkOrder>> Create([FromBody] WorkOrderInput input)
    {
        var workOrder = await _maintenance.CreateWorkOrderAsync(input);
        return workOrder is null
            ? NotFound($"Unknown asset {input.AssetId}")
            : CreatedAtAction(nameof(Get), new { id = workOrder.Id }, workOrder);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<WorkOrder>> Update(int id, [FromBody] WorkOrderInput input)
    {
        var workOrder = await _maintenance.UpdateWorkOrderAsync(id, input);
        return workOrder is null ? NotFound() : Ok(workOrder);
    }

    [HttpPost("{id:int}/start")]
    public async Task<ActionResult<WorkOrder>> Start(int id)
    {
        var workOrder = await _maintenance.StartWorkOrderAsync(id);
        return workOrder is null ? NotFound() : Ok(workOrder);
    }

    [HttpPost("{id:int}/complete")]
    public async Task<ActionResult<MaintenanceCompletionResult>> Complete(int id, [FromQuery] double laborHours = 0)
    {
        var result = await _maintenance.CompleteWorkOrderAsync(id, laborHours);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id) =>
        await _maintenance.CancelWorkOrderAsync(id) ? NoContent() : NotFound();

    [HttpPost("{id:int}/parts")]
    public async Task<ActionResult<WorkOrderPart>> AddPart(int id, [FromBody] WorkOrderPartInput input)
    {
        var line = await _maintenance.AddPartToWorkOrderAsync(id, input);
        return line is null ? NotFound() : Ok(line);
    }

    [HttpPost("preventive-sweep")]
    public async Task<ActionResult<IEnumerable<WorkOrder>>> PreventiveSweep() =>
        Ok(await _maintenance.RunPreventiveMaintenanceSweepAsync());

    [HttpGet("overdue")]
    public async Task<ActionResult<IEnumerable<WorkOrder>>> Overdue() =>
        Ok(await _maintenance.GetOverdueWorkOrdersAsync());
}
