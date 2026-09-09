using FML.Api.Common;
using FML.Api.Data;
using FML.Api.MaintenanceScheduling;
using Microsoft.EntityFrameworkCore;

namespace FML.Api.Inventory;

public record PartInput(
    string Sku,
    string Name,
    string Category,
    string Supplier,
    decimal UnitCost,
    int QuantityOnHand,
    int ReorderPoint,
    int ReorderQuantity,
    int LeadTimeDays,
    string WarehouseCode);

public record ReorderResult(int PartId, string Sku, int OrderedQuantity, decimal TotalCost, int PurchaseOrderId);

public record PartDemand(int PartId, string Sku, string Name, int OnHand, int OpenWorkOrderDemand, int Shortfall);

public class InventoryService
{
    private readonly FmlDbContext _db;

    public InventoryService(FmlDbContext db)
    {
        _db = db;
    }

    public Task<List<Part>> GetPartsAsync(string? category = null)
    {
        var query = _db.Parts.AsQueryable();
        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(p => p.Category == category);
        }

        return query.OrderBy(p => p.Sku).ToListAsync();
    }

    public Task<Part?> GetPartAsync(int id) =>
        _db.Parts.FirstOrDefaultAsync(p => p.Id == id);

    public async Task<Part> CreatePartAsync(PartInput input)
    {
        var part = new Part
        {
            Sku = input.Sku,
            Name = input.Name,
            Category = input.Category,
            Supplier = input.Supplier,
            UnitCost = input.UnitCost,
            QuantityOnHand = input.QuantityOnHand,
            ReorderPoint = input.ReorderPoint,
            ReorderQuantity = input.ReorderQuantity,
            LeadTimeDays = input.LeadTimeDays,
            WarehouseCode = input.WarehouseCode,
        };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        return part;
    }

    public async Task<Part?> UpdatePartAsync(int id, PartInput input)
    {
        var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == id);
        if (part is null)
        {
            return null;
        }

        part.Sku = input.Sku;
        part.Name = input.Name;
        part.Category = input.Category;
        part.Supplier = input.Supplier;
        part.UnitCost = input.UnitCost;
        part.QuantityOnHand = input.QuantityOnHand;
        part.ReorderPoint = input.ReorderPoint;
        part.ReorderQuantity = input.ReorderQuantity;
        part.LeadTimeDays = input.LeadTimeDays;
        part.WarehouseCode = input.WarehouseCode;
        await _db.SaveChangesAsync();
        return part;
    }

    public async Task<bool> DeletePartAsync(int id)
    {
        var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == id);
        if (part is null)
        {
            return false;
        }

        _db.Parts.Remove(part);
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Parts at or below their reorder point, taking demand from open work orders
    /// into account. Reads MaintenanceScheduling's tables directly.
    /// </summary>
    public async Task<List<PartDemand>> GetPartsBelowReorderPointAsync()
    {
        var demandByPart = await _db.WorkOrderParts
            .Where(wp => wp.WorkOrder!.Status != WorkOrderStatus.Completed
                         && wp.WorkOrder.Status != WorkOrderStatus.Cancelled)
            .GroupBy(wp => wp.PartId)
            .Select(g => new { PartId = g.Key, Demand = g.Sum(wp => wp.QuantityRequired - wp.QuantityIssued) })
            .ToDictionaryAsync(x => x.PartId, x => x.Demand);

        var parts = await _db.Parts.OrderBy(p => p.Sku).ToListAsync();
        var result = new List<PartDemand>();
        foreach (var part in parts)
        {
            var demand = demandByPart.TryGetValue(part.Id, out var d) ? d : 0;
            var shortfall = Math.Max(0, part.ReorderPoint + demand - part.QuantityOnHand);
            if (part.QuantityOnHand <= part.ReorderPoint || shortfall > 0)
            {
                result.Add(new PartDemand(part.Id, part.Sku, part.Name, part.QuantityOnHand, demand, shortfall));
            }
        }

        return result;
    }

    /// <summary>Raises a purchase order for a single part. Quantity defaults to the reorder policy.</summary>
    public async Task<ReorderResult?> ReorderPartAsync(int partId, int? quantity = null)
    {
        using var activity = FmlTelemetry.ActivitySource.StartActivity("Inventory.Reorder");
        var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == partId);
        if (part is null)
        {
            return null;
        }

        var orderQuantity = quantity ?? Math.Max(
            part.ReorderQuantity,
            (part.ReorderPoint - part.QuantityOnHand) * FmlConfig.DefaultReorderMultiplier);
        if (orderQuantity <= 0)
        {
            orderQuantity = part.ReorderQuantity;
        }

        var leadTime = part.LeadTimeDays > 0 ? part.LeadTimeDays : FmlConfig.PurchaseOrderLeadTimeDaysFallback;
        var purchaseOrder = new PurchaseOrder
        {
            PartId = part.Id,
            Quantity = orderQuantity,
            UnitCost = part.UnitCost,
            TotalCost = part.UnitCost * orderQuantity,
            Status = PurchaseOrderStatus.Placed,
            Reference = $"PO-{DateTime.UtcNow:yyyyMMddHHmmss}-{part.Sku}",
            ExpectedUtc = DateTime.UtcNow.AddDays(leadTime),
        };
        _db.PurchaseOrders.Add(purchaseOrder);
        await _db.SaveChangesAsync();

        FmlTelemetry.PartsReordered.Add(1);
        activity?.SetTag("fml.part.sku", part.Sku);
        return new ReorderResult(part.Id, part.Sku, orderQuantity, purchaseOrder.TotalCost, purchaseOrder.Id);
    }

    /// <summary>Reorders every part below its reorder point in one pass.</summary>
    public async Task<List<ReorderResult>> ReorderAllBelowReorderPointAsync()
    {
        var results = new List<ReorderResult>();
        foreach (var demand in await GetPartsBelowReorderPointAsync())
        {
            var result = await ReorderPartAsync(demand.PartId, demand.Shortfall > 0 ? demand.Shortfall : null);
            if (result is not null)
            {
                results.Add(result);
            }
        }

        return results;
    }

    public async Task<PurchaseOrder?> ReceivePurchaseOrderAsync(int purchaseOrderId)
    {
        var order = await _db.PurchaseOrders.Include(p => p.Part).FirstOrDefaultAsync(p => p.Id == purchaseOrderId);
        if (order is null || order.Status == PurchaseOrderStatus.Received)
        {
            return order;
        }

        order.Status = PurchaseOrderStatus.Received;
        order.ReceivedUtc = DateTime.UtcNow;
        order.Part!.QuantityOnHand += order.Quantity;
        _db.StockMovements.Add(new StockMovement
        {
            PartId = order.PartId,
            Quantity = order.Quantity,
            Reason = $"Receipt of {order.Reference}",
        });
        await _db.SaveChangesAsync();
        return order;
    }

    /// <summary>
    /// Issues the parts a work order needs. Called directly by MaintenanceScheduling
    /// when a work order is completed.
    /// </summary>
    public async Task<List<StockMovement>> IssuePartsToWorkOrderAsync(int workOrderId)
    {
        var lines = await _db.WorkOrderParts
            .Include(wp => wp.Part)
            .Where(wp => wp.WorkOrderId == workOrderId)
            .ToListAsync();

        var movements = new List<StockMovement>();
        foreach (var line in lines)
        {
            var outstanding = line.QuantityRequired - line.QuantityIssued;
            if (outstanding <= 0 || line.Part is null)
            {
                continue;
            }

            var issued = Math.Min(outstanding, line.Part.QuantityOnHand);
            if (issued <= 0)
            {
                continue;
            }

            line.Part.QuantityOnHand -= issued;
            line.QuantityIssued += issued;
            var movement = new StockMovement
            {
                PartId = line.PartId,
                Quantity = -issued,
                Reason = "Issued to work order",
                WorkOrderId = workOrderId,
            };
            _db.StockMovements.Add(movement);
            movements.Add(movement);
        }

        await _db.SaveChangesAsync();
        return movements;
    }

    public Task<List<StockMovement>> GetMovementsAsync(int? partId = null)
    {
        var query = _db.StockMovements.AsQueryable();
        if (partId.HasValue)
        {
            query = query.Where(m => m.PartId == partId.Value);
        }

        return query.OrderByDescending(m => m.OccurredUtc).Take(200).ToListAsync();
    }

    public Task<List<PurchaseOrder>> GetPurchaseOrdersAsync(PurchaseOrderStatus? status = null)
    {
        var query = _db.PurchaseOrders.AsQueryable();
        if (status.HasValue)
        {
            query = query.Where(p => p.Status == status.Value);
        }

        return query.OrderByDescending(p => p.CreatedUtc).ToListAsync();
    }

    public async Task<decimal> GetStockValueAsync() =>
        await _db.Parts.SumAsync(p => p.UnitCost * p.QuantityOnHand);
}
