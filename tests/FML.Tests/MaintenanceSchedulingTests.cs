using FML.Api.AssetRegistry;
using FML.Api.MaintenanceScheduling;

namespace FML.Tests;

public class MaintenanceSchedulingTests
{
    [Fact]
    public async Task Create_assigns_an_available_technician_and_a_due_date()
    {
        using var ctx = new FmlTestContext();
        var technician = await ctx.CreateTechnicianAsync();
        var asset = await ctx.CreateAssetAsync();

        var workOrder = await ctx.Maintenance.CreateWorkOrderAsync(new WorkOrderInput(
            asset.Id,
            "Replace fuel filter",
            "Routine filter swap",
            WorkOrderPriority.Normal,
            MaintenanceTrigger.Manual,
            null,
            null,
            null));

        Assert.NotNull(workOrder);
        Assert.Equal(WorkOrderStatus.Open, workOrder!.Status);
        Assert.Equal(technician.Id, workOrder.AssignedToUserId);
        Assert.True(workOrder.ScheduledForUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task Scheduling_a_date_marks_the_work_order_scheduled()
    {
        using var ctx = new FmlTestContext();
        var asset = await ctx.CreateAssetAsync();

        var workOrder = await ctx.Maintenance.CreateWorkOrderAsync(new WorkOrderInput(
            asset.Id,
            "Annual survey",
            "Class survey",
            WorkOrderPriority.High,
            MaintenanceTrigger.Preventive,
            DateTime.UtcNow.AddDays(20),
            null,
            null));

        Assert.Equal(WorkOrderStatus.Scheduled, workOrder!.Status);
    }

    [Fact]
    public async Task Create_for_unknown_asset_is_rejected()
    {
        using var ctx = new FmlTestContext();

        var workOrder = await ctx.Maintenance.CreateWorkOrderAsync(new WorkOrderInput(
            999, "x", "y", WorkOrderPriority.Low, MaintenanceTrigger.Manual, null, null, null));

        Assert.Null(workOrder);
    }

    [Fact]
    public async Task Start_puts_the_asset_under_maintenance()
    {
        using var ctx = new FmlTestContext();
        var asset = await ctx.CreateAssetAsync();
        var workOrder = await ctx.Maintenance.CreateWorkOrderAsync(new WorkOrderInput(
            asset.Id, "Inspect", "", WorkOrderPriority.Normal, MaintenanceTrigger.Manual, null, null, null));

        var started = await ctx.Maintenance.StartWorkOrderAsync(workOrder!.Id);

        Assert.Equal(WorkOrderStatus.InProgress, started!.Status);
        Assert.Equal(AssetStatus.UnderMaintenance, (await ctx.Assets.GetAsync(asset.Id))!.Status);
    }

    [Fact]
    public async Task Complete_issues_parts_restores_the_asset_and_reorders_low_stock()
    {
        using var ctx = new FmlTestContext();
        var asset = await ctx.CreateAssetAsync();
        var part = await ctx.CreatePartAsync(onHand: 6, reorderPoint: 4, reorderQuantity: 12);
        var workOrder = await ctx.Maintenance.CreateWorkOrderAsync(new WorkOrderInput(
            asset.Id,
            "Overhaul",
            "",
            WorkOrderPriority.High,
            MaintenanceTrigger.Corrective,
            null,
            null,
            new List<WorkOrderPartInput> { new(part.Id, 3) }));

        await ctx.Maintenance.StartWorkOrderAsync(workOrder!.Id);
        var result = await ctx.Maintenance.CompleteWorkOrderAsync(workOrder.Id, laborHours: 6.5);

        Assert.NotNull(result);
        Assert.Equal(WorkOrderStatus.Completed, result!.WorkOrder.Status);
        Assert.Equal(6.5, result.WorkOrder.LaborHours);
        Assert.NotNull(result.WorkOrder.CompletedUtc);
        Assert.Equal(1, result.PartLinesIssued);
        Assert.Equal(1, result.PartsReordered);
        Assert.Equal(3, (await ctx.Inventory.GetPartAsync(part.Id))!.QuantityOnHand);
        Assert.Equal(AssetStatus.InService, (await ctx.Assets.GetAsync(asset.Id))!.Status);
        Assert.Single(await ctx.Inventory.GetPurchaseOrdersAsync());
    }

    [Fact]
    public async Task Preventive_sweep_creates_one_work_order_per_due_asset()
    {
        using var ctx = new FmlTestContext();
        await ctx.CreateAssetAsync("ENG-910", operatingHours: 100);
        await ctx.CreateAssetAsync("ENG-911", operatingHours: 8000);

        var created = await ctx.Maintenance.RunPreventiveMaintenanceSweepAsync();
        var secondPass = await ctx.Maintenance.RunPreventiveMaintenanceSweepAsync();

        Assert.Single(created);
        Assert.Equal(MaintenanceTrigger.Preventive, created[0].Trigger);
        Assert.Empty(secondPass);
    }

    [Fact]
    public async Task Parts_can_be_added_after_creation_and_cancellation_is_recorded()
    {
        using var ctx = new FmlTestContext();
        var asset = await ctx.CreateAssetAsync();
        var part = await ctx.CreatePartAsync();
        var workOrder = await ctx.Maintenance.CreateWorkOrderAsync(new WorkOrderInput(
            asset.Id, "Service", "", WorkOrderPriority.Normal, MaintenanceTrigger.Manual, null, null, null));

        var line = await ctx.Maintenance.AddPartToWorkOrderAsync(workOrder!.Id, new WorkOrderPartInput(part.Id, 2));
        Assert.NotNull(line);
        Assert.Equal(2, line!.QuantityRequired);

        Assert.True(await ctx.Maintenance.CancelWorkOrderAsync(workOrder.Id));
        Assert.Equal(WorkOrderStatus.Cancelled, (await ctx.Maintenance.GetWorkOrderAsync(workOrder.Id))!.Status);
    }

    [Fact]
    public async Task Overdue_work_orders_are_listed()
    {
        using var ctx = new FmlTestContext();
        var asset = await ctx.CreateAssetAsync();
        var overdue = await ctx.Maintenance.CreateWorkOrderAsync(new WorkOrderInput(
            asset.Id,
            "Late job",
            "",
            WorkOrderPriority.High,
            MaintenanceTrigger.Manual,
            DateTime.UtcNow.AddDays(-3),
            null,
            null));
        await ctx.Maintenance.CreateWorkOrderAsync(new WorkOrderInput(
            asset.Id,
            "Future job",
            "",
            WorkOrderPriority.Low,
            MaintenanceTrigger.Manual,
            DateTime.UtcNow.AddDays(3),
            null,
            null));

        var listed = await ctx.Maintenance.GetOverdueWorkOrdersAsync();

        Assert.Equal(new[] { overdue!.Id }, listed.Select(w => w.Id));
    }
}
