using FML.Api.AssetRegistry;
using FML.Api.Auth;
using FML.Api.Data;
using FML.Api.Inventory;
using FML.Api.MaintenanceScheduling;
using FML.Api.Reporting;
using FML.Api.Telemetry;
using FML.Common.Auth;
using Microsoft.EntityFrameworkCore;

namespace FML.Tests;

/// <summary>
/// Builds the whole monolith's service graph over an isolated in-memory database.
/// Because the modules reference each other directly there is nothing to stub:
/// every test exercises the real cross-module behavior.
/// </summary>
public sealed class FmlTestContext : IDisposable
{
    public FmlTestContext(bool seed = false)
    {
        Db = new FmlDbContext(new DbContextOptionsBuilder<FmlDbContext>()
            .UseInMemoryDatabase($"fml-{Guid.NewGuid()}")
            .Options);

        Auth = new AuthService(Db);
        Assets = new AssetService(Db);
        Inventory = new InventoryService(Db);
        Maintenance = new MaintenanceService(Db, Assets, Inventory, Auth);
        Telemetry = new TelemetryService(Db, Maintenance, Assets);
        Reports = new ReportService(Db, Maintenance, Inventory, Telemetry);

        if (seed)
        {
            SeedData.Seed(Db);
        }
    }

    public FmlDbContext Db { get; }

    public AuthService Auth { get; }

    public AssetService Assets { get; }

    public InventoryService Inventory { get; }

    public MaintenanceService Maintenance { get; }

    public TelemetryService Telemetry { get; }

    public ReportService Reports { get; }

    public Task<Asset> CreateAssetAsync(string code = "TST-001", double operatingHours = 100) =>
        Assets.CreateAsync(new AssetInput(
            code,
            $"Test asset {code}",
            AssetType.Engine,
            AssetStatus.InService,
            "Wartsila",
            "W-31DF",
            "Rotterdam",
            new DateTime(2019, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            operatingHours,
            0,
            3000));

    public Task<Part> CreatePartAsync(
        string sku = "PRT-001",
        int onHand = 10,
        int reorderPoint = 4,
        int reorderQuantity = 12) =>
        Inventory.CreatePartAsync(new PartInput(
            sku,
            $"Part {sku}",
            "Filters",
            "Wartsila Parts",
            125.50m,
            onHand,
            reorderPoint,
            reorderQuantity,
            10,
            "WH-1"));

    public Task<User> CreateTechnicianAsync(string username = "tech.test") =>
        Auth.CreateUserAsync(new CreateUserRequest(username, $"{username}@fml.local", "pw", UserRole.Technician));

    public void Dispose() => Db.Dispose();
}
