namespace FML.Api.Common;

/// <summary>
/// Shared, process-wide configuration for every FML module. Populated once at
/// startup from appsettings/environment and read directly by services.
/// </summary>
public static class FmlConfig
{
    public static string ConnectionString { get; private set; } =
        "Host=localhost;Port=5432;Database=fml;Username=fml;Password=fml";

    public static string FleetName { get; private set; } = "Northsea Fleet";
    public static bool AutoMigrateOnStartup { get; private set; } = true;
    public static bool SeedSampleData { get; private set; } = true;

    // Telemetry / maintenance thresholds. Read by Telemetry, MaintenanceScheduling and Reporting.
    public static double EngineTempCriticalC { get; private set; } = 95.0;
    public static double VibrationCriticalMm { get; private set; } = 7.5;
    public static double OilPressureMinBar { get; private set; } = 2.0;
    public static int PreventiveMaintenanceIntervalHours { get; private set; } = 2000;
    public static int WorkOrderDueInDays { get; private set; } = 7;

    // Inventory knobs, also consulted by MaintenanceScheduling when it reserves parts.
    public static int DefaultReorderMultiplier { get; private set; } = 2;
    public static int PurchaseOrderLeadTimeDaysFallback { get; private set; } = 14;
    public static decimal StockValueAlertThreshold { get; private set; } = 250_000m;

    // Auth
    public static string PasswordSalt { get; private set; } = "fml-dev-salt";
    public static int TokenLifetimeMinutes { get; private set; } = 480;

    public static void Load(IConfiguration configuration)
    {
        ConnectionString = configuration.GetConnectionString("Fml") ?? ConnectionString;
        FleetName = configuration["Fml:FleetName"] ?? FleetName;
        AutoMigrateOnStartup = GetBool(configuration, "Fml:AutoMigrateOnStartup", AutoMigrateOnStartup);
        SeedSampleData = GetBool(configuration, "Fml:SeedSampleData", SeedSampleData);

        EngineTempCriticalC = GetDouble(configuration, "Fml:Thresholds:EngineTempCriticalC", EngineTempCriticalC);
        VibrationCriticalMm = GetDouble(configuration, "Fml:Thresholds:VibrationCriticalMm", VibrationCriticalMm);
        OilPressureMinBar = GetDouble(configuration, "Fml:Thresholds:OilPressureMinBar", OilPressureMinBar);
        PreventiveMaintenanceIntervalHours = GetInt(configuration, "Fml:Maintenance:PreventiveIntervalHours", PreventiveMaintenanceIntervalHours);
        WorkOrderDueInDays = GetInt(configuration, "Fml:Maintenance:WorkOrderDueInDays", WorkOrderDueInDays);

        DefaultReorderMultiplier = GetInt(configuration, "Fml:Inventory:DefaultReorderMultiplier", DefaultReorderMultiplier);
        PurchaseOrderLeadTimeDaysFallback = GetInt(configuration, "Fml:Inventory:LeadTimeDaysFallback", PurchaseOrderLeadTimeDaysFallback);
        StockValueAlertThreshold = (decimal)GetDouble(configuration, "Fml:Inventory:StockValueAlertThreshold", (double)StockValueAlertThreshold);

        PasswordSalt = configuration["Fml:Auth:PasswordSalt"] ?? PasswordSalt;
        TokenLifetimeMinutes = GetInt(configuration, "Fml:Auth:TokenLifetimeMinutes", TokenLifetimeMinutes);
    }

    /// <summary>Threshold lookup used by Telemetry ingestion and by Reporting alert counts.</summary>
    public static bool IsBreach(string metric, double value) => metric switch
    {
        "EngineTempC" => value >= EngineTempCriticalC,
        "VibrationMm" => value >= VibrationCriticalMm,
        "OilPressureBar" => value <= OilPressureMinBar,
        _ => false,
    };

    private static bool GetBool(IConfiguration c, string key, bool fallback) =>
        bool.TryParse(c[key], out var parsed) ? parsed : fallback;

    private static int GetInt(IConfiguration c, string key, int fallback) =>
        int.TryParse(c[key], out var parsed) ? parsed : fallback;

    private static double GetDouble(IConfiguration c, string key, double fallback) =>
        double.TryParse(c[key], out var parsed) ? parsed : fallback;
}
