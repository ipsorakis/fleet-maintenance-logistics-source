using FML.Api.AssetRegistry;
using FML.Api.Auth;
using FML.Api.Inventory;
using FML.Api.MaintenanceScheduling;
using FML.Api.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace FML.Api.Data;

/// <summary>Sample fleet used for local development, demos and the test baseline.</summary>
public static class SeedData
{
    private static readonly DateTime Anchor = new(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc);

    public static void Seed(FmlDbContext db)
    {
        if (db.Assets.Any())
        {
            return;
        }

        var users = new[]
        {
            NewUser("admin", "admin@fml.local", UserRole.Administrator),
            NewUser("planner", "planner@fml.local", UserRole.Planner),
            NewUser("tech.lars", "lars@fml.local", UserRole.Technician),
            NewUser("tech.mira", "mira@fml.local", UserRole.Technician),
            NewUser("viewer", "viewer@fml.local", UserRole.Viewer),
        };
        db.Users.AddRange(users);

        var assets = new[]
        {
            NewAsset("VSL-001", "MV Northern Star", AssetType.Vessel, AssetStatus.InService, "Damen", "CSD-650", "Rotterdam", 2016, 18_450, 6500, 4200),
            NewAsset("VSL-002", "MV Baltic Trader", AssetType.Vessel, AssetStatus.InService, "Damen", "CSD-450", "Gdansk", 2018, 12_300, 4500, 3100),
            NewAsset("VSL-003", "MV Fjord Runner", AssetType.Vessel, AssetStatus.UnderMaintenance, "Ulstein", "PX-121", "Bergen", 2014, 26_780, 3800, 2900),
            NewAsset("ENG-101", "Main Engine A - Northern Star", AssetType.Engine, AssetStatus.InService, "Wartsila", "W-31DF", "Rotterdam", 2016, 18_450, 0, 4200),
            NewAsset("ENG-102", "Main Engine B - Baltic Trader", AssetType.Engine, AssetStatus.InService, "MAN", "6L32/44CR", "Gdansk", 2018, 12_260, 0, 3480),
            NewAsset("ENG-103", "Aux Engine - Fjord Runner", AssetType.Engine, AssetStatus.Standby, "Wartsila", "W-20", "Bergen", 2014, 9_940, 0, 1200),
            NewAsset("CRN-201", "Deck Crane Port", AssetType.Crane, AssetStatus.InService, "Liebherr", "CBG-300", "Rotterdam", 2017, 7_120, 30, 250),
            NewAsset("CRN-202", "Deck Crane Starboard", AssetType.Crane, AssetStatus.InService, "Liebherr", "CBG-300", "Rotterdam", 2017, 6_880, 30, 250),
            NewAsset("PMP-301", "Ballast Pump 1", AssetType.Pump, AssetStatus.InService, "Grundfos", "NK-200", "Gdansk", 2019, 4_310, 0, 75),
            NewAsset("GEN-401", "Emergency Generator", AssetType.Generator, AssetStatus.Standby, "Caterpillar", "C18", "Bergen", 2015, 2_180, 0, 600),
        };
        db.Assets.AddRange(assets);

        var parts = new[]
        {
            NewPart("FLT-OIL-01", "Lube oil filter element", "Filters", "MarineSupply BV", 78.50m, 42, 20, 40, 10, "WH-ROT"),
            NewPart("FLT-FUEL-02", "Fuel filter cartridge", "Filters", "MarineSupply BV", 64.00m, 12, 15, 30, 12, "WH-ROT"),
            NewPart("BRG-MAIN-11", "Main bearing shell set", "Bearings", "Wartsila Parts", 1_420.00m, 4, 4, 6, 35, "WH-ROT"),
            NewPart("SEAL-PMP-07", "Pump mechanical seal", "Seals", "Grundfos Service", 310.25m, 9, 6, 12, 21, "WH-GDN"),
            NewPart("HYD-HOSE-04", "Hydraulic hose 3/4in", "Hydraulics", "Liebherr Service", 145.75m, 3, 8, 16, 14, "WH-ROT"),
            NewPart("INJ-NOZ-09", "Injector nozzle", "Fuel system", "MAN PrimeServ", 690.00m, 16, 10, 20, 28, "WH-GDN"),
            NewPart("BLT-ANCH-22", "Anchor bolt M36", "Fasteners", "NordicFasteners", 22.40m, 120, 60, 120, 7, "WH-BRG"),
            NewPart("GSK-EXH-15", "Exhaust manifold gasket", "Gaskets", "Wartsila Parts", 96.80m, 7, 10, 20, 18, "WH-BRG"),
        };
        db.Parts.AddRange(parts);
        db.SaveChanges();

        db.TelemetryReadings.AddRange(BuildReadings(assets));

        var planner = users.Single(u => u.Username == "planner");
        var lars = users.Single(u => u.Username == "tech.lars");
        var mira = users.Single(u => u.Username == "tech.mira");

        var workOrders = new[]
        {
            new WorkOrder
            {
                AssetId = assets[3].Id,
                Title = "2000h preventive service - Main Engine A",
                Description = "Replace lube oil and fuel filters, inspect injectors.",
                Status = WorkOrderStatus.Scheduled,
                Priority = WorkOrderPriority.Normal,
                Trigger = MaintenanceTrigger.Preventive,
                CreatedUtc = Anchor.AddDays(-12),
                ScheduledForUtc = Anchor.AddDays(3),
                AssignedToUserId = lars.Id,
                Parts =
                {
                    new WorkOrderPart { PartId = parts[0].Id, QuantityRequired = 4 },
                    new WorkOrderPart { PartId = parts[1].Id, QuantityRequired = 4 },
                },
            },
            new WorkOrder
            {
                AssetId = assets[2].Id,
                Title = "Hull inspection after grounding",
                Description = "Dry dock inspection of hull plating and rudder assembly.",
                Status = WorkOrderStatus.InProgress,
                Priority = WorkOrderPriority.High,
                Trigger = MaintenanceTrigger.Corrective,
                CreatedUtc = Anchor.AddDays(-6),
                ScheduledForUtc = Anchor.AddDays(-2),
                AssignedToUserId = mira.Id,
                LaborHours = 26,
            },
            new WorkOrder
            {
                AssetId = assets[6].Id,
                Title = "Replace hydraulic hose - Deck Crane Port",
                Description = "Hose weeping at boom cylinder; replace and pressure test.",
                Status = WorkOrderStatus.Open,
                Priority = WorkOrderPriority.High,
                Trigger = MaintenanceTrigger.Corrective,
                CreatedUtc = Anchor.AddDays(-3),
                ScheduledForUtc = Anchor.AddDays(1),
                AssignedToUserId = lars.Id,
                Parts = { new WorkOrderPart { PartId = parts[4].Id, QuantityRequired = 2 } },
            },
            new WorkOrder
            {
                AssetId = assets[8].Id,
                Title = "Ballast pump seal replacement",
                Description = "Condition-based: vibration trending upwards over three weeks.",
                Status = WorkOrderStatus.Completed,
                Priority = WorkOrderPriority.Normal,
                Trigger = MaintenanceTrigger.ConditionBased,
                CreatedUtc = Anchor.AddDays(-28),
                ScheduledForUtc = Anchor.AddDays(-21),
                CompletedUtc = Anchor.AddDays(-20),
                LaborHours = 9.5,
                AssignedToUserId = mira.Id,
                Parts = { new WorkOrderPart { PartId = parts[3].Id, QuantityRequired = 1, QuantityIssued = 1 } },
            },
            new WorkOrder
            {
                AssetId = assets[4].Id,
                Title = "Injector overhaul - Main Engine B",
                Description = "Overhaul six injectors, measure spray pattern.",
                Status = WorkOrderStatus.Open,
                Priority = WorkOrderPriority.Normal,
                Trigger = MaintenanceTrigger.Preventive,
                CreatedUtc = Anchor.AddDays(-1),
                ScheduledForUtc = Anchor.AddDays(9),
                AssignedToUserId = planner.Id,
                Parts = { new WorkOrderPart { PartId = parts[5].Id, QuantityRequired = 6 } },
            },
        };
        db.WorkOrders.AddRange(workOrders);
        db.SaveChanges();

        db.StockMovements.AddRange(
            new StockMovement { PartId = parts[3].Id, Quantity = -1, Reason = "Issued to work order", WorkOrderId = workOrders[3].Id, OccurredUtc = Anchor.AddDays(-21) },
            new StockMovement { PartId = parts[0].Id, Quantity = 40, Reason = "Purchase order receipt", OccurredUtc = Anchor.AddDays(-30) },
            new StockMovement { PartId = parts[4].Id, Quantity = -5, Reason = "Issued to work order", WorkOrderId = workOrders[1].Id, OccurredUtc = Anchor.AddDays(-5) });

        db.PurchaseOrders.AddRange(
            new PurchaseOrder
            {
                PartId = parts[4].Id,
                Quantity = 16,
                UnitCost = parts[4].UnitCost,
                TotalCost = parts[4].UnitCost * 16,
                Status = PurchaseOrderStatus.Placed,
                Reference = "PO-2026-0041",
                CreatedUtc = Anchor.AddDays(-4),
                ExpectedUtc = Anchor.AddDays(10),
            },
            new PurchaseOrder
            {
                PartId = parts[1].Id,
                Quantity = 30,
                UnitCost = parts[1].UnitCost,
                TotalCost = parts[1].UnitCost * 30,
                Status = PurchaseOrderStatus.Draft,
                Reference = "PO-2026-0042",
                CreatedUtc = Anchor.AddDays(-1),
                ExpectedUtc = Anchor.AddDays(11),
            });

        db.SaveChanges();
    }

    private static List<TelemetryReading> BuildReadings(Asset[] assets)
    {
        var readings = new List<TelemetryReading>();
        void Add(Asset asset, string metric, double value, string unit, int hoursAgo)
        {
            readings.Add(new TelemetryReading
            {
                AssetId = asset.Id,
                Metric = metric,
                Value = value,
                Unit = unit,
                Source = "sensor-sim",
                RecordedUtc = Anchor.AddHours(-hoursAgo),
                ThresholdBreached = Common.FmlConfig.IsBreach(metric, value),
            });
        }

        Add(assets[3], "EngineTempC", 78.4, "C", 36);
        Add(assets[3], "EngineTempC", 82.1, "C", 24);
        Add(assets[3], "EngineTempC", 88.9, "C", 12);
        Add(assets[3], "EngineTempC", 96.4, "C", 2);
        Add(assets[3], "OilPressureBar", 4.1, "bar", 12);
        Add(assets[3], "VibrationMm", 3.2, "mm/s", 12);
        Add(assets[4], "EngineTempC", 74.2, "C", 30);
        Add(assets[4], "EngineTempC", 76.8, "C", 18);
        Add(assets[4], "OilPressureBar", 3.9, "bar", 18);
        Add(assets[4], "OilPressureBar", 1.8, "bar", 3);
        Add(assets[4], "FuelRateLph", 640.5, "l/h", 3);
        Add(assets[5], "EngineTempC", 61.0, "C", 40);
        Add(assets[5], "VibrationMm", 2.1, "mm/s", 40);
        Add(assets[6], "VibrationMm", 5.4, "mm/s", 26);
        Add(assets[6], "VibrationMm", 6.9, "mm/s", 14);
        Add(assets[6], "HydraulicPressureBar", 172.0, "bar", 14);
        Add(assets[7], "VibrationMm", 2.8, "mm/s", 20);
        Add(assets[7], "HydraulicPressureBar", 168.4, "bar", 20);
        Add(assets[8], "VibrationMm", 7.9, "mm/s", 6);
        Add(assets[8], "FlowRateM3h", 240.0, "m3/h", 6);
        Add(assets[8], "OilPressureBar", 3.4, "bar", 6);
        Add(assets[9], "EngineTempC", 45.2, "C", 48);
        Add(assets[9], "FuelRateLph", 88.0, "l/h", 48);
        Add(assets[0], "FuelRateLph", 910.2, "l/h", 8);
        Add(assets[0], "SpeedKnots", 12.4, "kn", 8);
        Add(assets[1], "FuelRateLph", 705.6, "l/h", 9);
        Add(assets[1], "SpeedKnots", 11.1, "kn", 9);
        Add(assets[2], "SpeedKnots", 0.0, "kn", 10);
        return readings;
    }

    private static User NewUser(string username, string email, UserRole role) => new()
    {
        Username = username,
        Email = email,
        Role = role,
        PasswordHash = AuthService.HashPassword(username + "-pw"),
        CreatedUtc = Anchor.AddDays(-200),
    };

    private static Asset NewAsset(
        string code,
        string name,
        AssetType type,
        AssetStatus status,
        string manufacturer,
        string model,
        string homePort,
        int commissionedYear,
        double operatingHours,
        double capacityTonnes,
        double ratedPowerKw) => new()
        {
            Code = code,
            Name = name,
            AssetType = type,
            Status = status,
            Manufacturer = manufacturer,
            Model = model,
            HomePort = homePort,
            CommissionedOn = new DateTime(commissionedYear, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            OperatingHours = operatingHours,
            CapacityTonnes = capacityTonnes,
            RatedPowerKw = ratedPowerKw,
            LastServicedUtc = Anchor.AddDays(-90),
        };

    private static Part NewPart(
        string sku,
        string name,
        string category,
        string supplier,
        decimal unitCost,
        int onHand,
        int reorderPoint,
        int reorderQuantity,
        int leadTimeDays,
        string warehouse) => new()
        {
            Sku = sku,
            Name = name,
            Category = category,
            Supplier = supplier,
            UnitCost = unitCost,
            QuantityOnHand = onHand,
            ReorderPoint = reorderPoint,
            ReorderQuantity = reorderQuantity,
            LeadTimeDays = leadTimeDays,
            WarehouseCode = warehouse,
        };
}
