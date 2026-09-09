using System.Text.Json.Serialization;
using FML.Api.AssetRegistry;
using FML.Api.Auth;
using FML.Api.Common;
using FML.Api.Data;
using FML.Api.Inventory;
using FML.Api.MaintenanceScheduling;
using FML.Api.Reporting;
using FML.Api.Telemetry;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

FmlConfig.Load(builder.Configuration);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<FmlDbContext>(options => options.UseNpgsql(FmlConfig.ConnectionString));

// Every module's service is registered here; they reference each other directly.
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AssetService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<MaintenanceService>();
builder.Services.AddScoped<TelemetryService>();
builder.Services.AddScoped<ReportService>();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(FmlTelemetry.ServiceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource(FmlTelemetry.ServiceName)
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter(FmlTelemetry.ServiceName)
        .AddPrometheusExporter());

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.MapPrometheusScrapingEndpoint("/metrics");
app.MapGet("/health", () => Results.Ok(new { status = "ok", fleet = FmlConfig.FleetName }));

if (FmlConfig.AutoMigrateOnStartup)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<FmlDbContext>();
    db.Database.EnsureCreated();
    if (FmlConfig.SeedSampleData)
    {
        SeedData.Seed(db);
    }
}

app.Run();
