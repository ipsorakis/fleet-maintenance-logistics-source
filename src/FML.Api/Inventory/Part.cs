namespace FML.Api.Inventory;

public enum PurchaseOrderStatus
{
    Draft = 0,
    Placed = 1,
    Received = 2,
    Cancelled = 3,
}

public class Part
{
    public int Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Supplier { get; set; } = string.Empty;
    public decimal UnitCost { get; set; }
    public int QuantityOnHand { get; set; }
    public int ReorderPoint { get; set; }
    public int ReorderQuantity { get; set; }
    public int LeadTimeDays { get; set; }
    public string WarehouseCode { get; set; } = string.Empty;

    public List<StockMovement> Movements { get; set; } = new();
    public List<PurchaseOrder> PurchaseOrders { get; set; } = new();
}

public class StockMovement
{
    public int Id { get; set; }
    public int PartId { get; set; }
    public Part? Part { get; set; }

    /// <summary>Signed quantity: negative for issues to work orders, positive for receipts.</summary>
    public int Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int? WorkOrderId { get; set; }
    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
}

public class PurchaseOrder
{
    public int Id { get; set; }
    public int PartId { get; set; }
    public Part? Part { get; set; }

    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public string Reference { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpectedUtc { get; set; }
    public DateTime? ReceivedUtc { get; set; }
}
