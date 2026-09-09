using FML.Api.Inventory;
using FML.Api.MaintenanceScheduling;

namespace FML.Tests;

public class InventoryTests
{
    [Fact]
    public async Task Part_crud_round_trip()
    {
        using var ctx = new FmlTestContext();
        var part = await ctx.CreatePartAsync("PRT-100");

        var updated = await ctx.Inventory.UpdatePartAsync(part.Id, new PartInput(
            "PRT-100", "Updated filter", "Filters", "New supplier", 99.99m, 20, 5, 15, 7, "WH-2"));

        Assert.Equal("Updated filter", updated!.Name);
        Assert.Equal(20, updated.QuantityOnHand);
        Assert.Equal("WH-2", updated.WarehouseCode);
        Assert.True(await ctx.Inventory.DeletePartAsync(part.Id));
        Assert.Null(await ctx.Inventory.GetPartAsync(part.Id));
    }

    [Fact]
    public async Task Parts_at_or_below_reorder_point_are_candidates()
    {
        using var ctx = new FmlTestContext();
        await ctx.CreatePartAsync("PRT-101", onHand: 20, reorderPoint: 4);
        await ctx.CreatePartAsync("PRT-102", onHand: 4, reorderPoint: 4);

        var candidates = await ctx.Inventory.GetPartsBelowReorderPointAsync();

        Assert.Equal(new[] { "PRT-102" }, candidates.Select(c => c.Sku));
        Assert.Equal(0, candidates[0].OpenWorkOrderDemand);
    }

    [Fact]
    public async Task Open_work_order_demand_pulls_a_healthy_part_into_the_reorder_run()
    {
        using var ctx = new FmlTestContext();
        var asset = await ctx.CreateAssetAsync();
        var part = await ctx.CreatePartAsync("PRT-103", onHand: 10, reorderPoint: 6, reorderQuantity: 10);
        await ctx.Maintenance.CreateWorkOrderAsync(new WorkOrderInput(
            asset.Id,
            "Big job",
            "",
            WorkOrderPriority.High,
            MaintenanceTrigger.Manual,
            null,
            null,
            new List<WorkOrderPartInput> { new(part.Id, 9) }));

        var candidates = await ctx.Inventory.GetPartsBelowReorderPointAsync();

        var candidate = Assert.Single(candidates);
        Assert.Equal("PRT-103", candidate.Sku);
        Assert.Equal(9, candidate.OpenWorkOrderDemand);
        Assert.Equal(5, candidate.Shortfall);
    }

    [Fact]
    public async Task Cancelled_work_orders_no_longer_count_as_demand()
    {
        using var ctx = new FmlTestContext();
        var asset = await ctx.CreateAssetAsync();
        var part = await ctx.CreatePartAsync("PRT-104", onHand: 12, reorderPoint: 4, reorderQuantity: 10);
        var workOrder = await ctx.Maintenance.CreateWorkOrderAsync(new WorkOrderInput(
            asset.Id,
            "Job",
            "",
            WorkOrderPriority.Normal,
            MaintenanceTrigger.Manual,
            null,
            null,
            new List<WorkOrderPartInput> { new(part.Id, 9) }));

        Assert.Single(await ctx.Inventory.GetPartsBelowReorderPointAsync());

        await ctx.Maintenance.CancelWorkOrderAsync(workOrder!.Id);

        Assert.Empty(await ctx.Inventory.GetPartsBelowReorderPointAsync());
    }

    [Fact]
    public async Task Reorder_raises_a_purchase_order_using_the_reorder_policy()
    {
        using var ctx = new FmlTestContext();
        var part = await ctx.CreatePartAsync("PRT-105", onHand: 2, reorderPoint: 8, reorderQuantity: 10);

        var result = await ctx.Inventory.ReorderPartAsync(part.Id);

        Assert.NotNull(result);
        Assert.Equal(12, result!.OrderedQuantity); // (8 - 2) * multiplier 2 beats the reorder quantity
        Assert.Equal(part.UnitCost * 12, result.TotalCost);

        var purchaseOrder = Assert.Single(await ctx.Inventory.GetPurchaseOrdersAsync(PurchaseOrderStatus.Placed));
        Assert.Equal(part.Id, purchaseOrder.PartId);
        Assert.True(purchaseOrder.ExpectedUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task Reorder_run_covers_every_candidate_and_receipt_restocks()
    {
        using var ctx = new FmlTestContext();
        await ctx.CreatePartAsync("PRT-106", onHand: 1, reorderPoint: 5, reorderQuantity: 10);
        await ctx.CreatePartAsync("PRT-107", onHand: 50, reorderPoint: 5, reorderQuantity: 10);

        var results = await ctx.Inventory.ReorderAllBelowReorderPointAsync();
        var order = Assert.Single(await ctx.Inventory.GetPurchaseOrdersAsync());
        var received = await ctx.Inventory.ReceivePurchaseOrderAsync(order.Id);

        Assert.Single(results);
        Assert.Equal(PurchaseOrderStatus.Received, received!.Status);
        Assert.NotNull(received.ReceivedUtc);
        Assert.Equal(1 + order.Quantity, (await ctx.Inventory.GetPartAsync(order.PartId))!.QuantityOnHand);
        Assert.Single(await ctx.Inventory.GetMovementsAsync(order.PartId));
    }

    [Fact]
    public async Task Issuing_parts_is_capped_by_stock_on_hand()
    {
        using var ctx = new FmlTestContext();
        var asset = await ctx.CreateAssetAsync();
        var part = await ctx.CreatePartAsync("PRT-108", onHand: 2, reorderPoint: 1, reorderQuantity: 5);
        var workOrder = await ctx.Maintenance.CreateWorkOrderAsync(new WorkOrderInput(
            asset.Id,
            "Job",
            "",
            WorkOrderPriority.Normal,
            MaintenanceTrigger.Manual,
            null,
            null,
            new List<WorkOrderPartInput> { new(part.Id, 5) }));

        var movements = await ctx.Inventory.IssuePartsToWorkOrderAsync(workOrder!.Id);

        Assert.Equal(-2, Assert.Single(movements).Quantity);
        Assert.Equal(0, (await ctx.Inventory.GetPartAsync(part.Id))!.QuantityOnHand);
        Assert.Empty(await ctx.Inventory.IssuePartsToWorkOrderAsync(workOrder.Id));
    }

    [Fact]
    public async Task Stock_value_sums_cost_times_quantity()
    {
        using var ctx = new FmlTestContext();
        var part = await ctx.CreatePartAsync("PRT-109", onHand: 4);

        Assert.Equal(part.UnitCost * 4, await ctx.Inventory.GetStockValueAsync());
    }
}
