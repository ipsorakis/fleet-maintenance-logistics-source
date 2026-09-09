using FML.Api.AssetRegistry;
using FML.Api.Auth;
using FML.Api.Inventory;
using FML.Api.MaintenanceScheduling;
using FML.Api.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace FML.Api.Data;

/// <summary>
/// The single database context for the whole application. Every module maps its
/// tables here and every service takes a dependency on it.
/// </summary>
public class FmlDbContext : DbContext
{
    public FmlDbContext(DbContextOptions<FmlDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<TelemetryReading> TelemetryReadings => Set<TelemetryReading>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderPart> WorkOrderParts => Set<WorkOrderPart>();
    public DbSet<Part> Parts => Set<Part>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Username).HasMaxLength(64).IsRequired();
            e.Property(u => u.Email).HasMaxLength(200);
            e.Property(u => u.PasswordHash).HasMaxLength(128);
        });

        modelBuilder.Entity<Asset>(e =>
        {
            e.ToTable("assets");
            e.HasIndex(a => a.Code).IsUnique();
            e.Property(a => a.Code).HasMaxLength(32).IsRequired();
            e.Property(a => a.Name).HasMaxLength(160).IsRequired();
            e.Property(a => a.Manufacturer).HasMaxLength(120);
            e.Property(a => a.Model).HasMaxLength(120);
            e.Property(a => a.HomePort).HasMaxLength(80);
        });

        modelBuilder.Entity<TelemetryReading>(e =>
        {
            e.ToTable("telemetry_readings");
            e.Property(r => r.Metric).HasMaxLength(64).IsRequired();
            e.Property(r => r.Unit).HasMaxLength(24);
            e.Property(r => r.Source).HasMaxLength(64);
            e.HasIndex(r => new { r.AssetId, r.Metric, r.RecordedUtc });
            e.HasOne(r => r.Asset)
                .WithMany(a => a.Readings)
                .HasForeignKey(r => r.AssetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkOrder>(e =>
        {
            e.ToTable("work_orders");
            e.Property(w => w.Title).HasMaxLength(200).IsRequired();
            e.Property(w => w.Description).HasMaxLength(2000);
            e.HasIndex(w => new { w.AssetId, w.Status });
            e.HasOne(w => w.Asset)
                .WithMany(a => a.WorkOrders)
                .HasForeignKey(w => w.AssetId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(w => w.AssignedTo)
                .WithMany()
                .HasForeignKey(w => w.AssignedToUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<WorkOrderPart>(e =>
        {
            e.ToTable("work_order_parts");
            e.HasIndex(p => new { p.WorkOrderId, p.PartId }).IsUnique();
            e.HasOne(p => p.WorkOrder)
                .WithMany(w => w.Parts)
                .HasForeignKey(p => p.WorkOrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Part)
                .WithMany()
                .HasForeignKey(p => p.PartId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Part>(e =>
        {
            e.ToTable("parts");
            e.HasIndex(p => p.Sku).IsUnique();
            e.Property(p => p.Sku).HasMaxLength(32).IsRequired();
            e.Property(p => p.Name).HasMaxLength(160).IsRequired();
            e.Property(p => p.Category).HasMaxLength(80);
            e.Property(p => p.Supplier).HasMaxLength(120);
            e.Property(p => p.WarehouseCode).HasMaxLength(32);
            e.Property(p => p.UnitCost).HasPrecision(12, 2);
        });

        modelBuilder.Entity<StockMovement>(e =>
        {
            e.ToTable("stock_movements");
            e.Property(m => m.Reason).HasMaxLength(200);
            e.HasOne(m => m.Part)
                .WithMany(p => p.Movements)
                .HasForeignKey(m => m.PartId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PurchaseOrder>(e =>
        {
            e.ToTable("purchase_orders");
            e.Property(p => p.Reference).HasMaxLength(64);
            e.Property(p => p.UnitCost).HasPrecision(12, 2);
            e.Property(p => p.TotalCost).HasPrecision(14, 2);
            e.HasOne(p => p.Part)
                .WithMany(p => p.PurchaseOrders)
                .HasForeignKey(p => p.PartId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
