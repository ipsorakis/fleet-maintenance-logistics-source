using FML.Api.Common;
using FML.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FML.Api.AssetRegistry;

public record AssetInput(
    string Code,
    string Name,
    AssetType AssetType,
    AssetStatus Status,
    string Manufacturer,
    string Model,
    string HomePort,
    DateTime CommissionedOn,
    double OperatingHours,
    double CapacityTonnes,
    double RatedPowerKw);

public class AssetService
{
    private readonly FmlDbContext _db;

    public AssetService(FmlDbContext db)
    {
        _db = db;
    }

    public Task<List<Asset>> GetAllAsync(AssetStatus? status = null, AssetType? type = null)
    {
        var query = _db.Assets.AsQueryable();
        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status.Value);
        }

        if (type.HasValue)
        {
            query = query.Where(a => a.AssetType == type.Value);
        }

        return query.OrderBy(a => a.Code).ToListAsync();
    }

    public Task<Asset?> GetAsync(int id) =>
        _db.Assets.FirstOrDefaultAsync(a => a.Id == id);

    public Task<Asset?> GetByCodeAsync(string code) =>
        _db.Assets.FirstOrDefaultAsync(a => a.Code == code);

    public async Task<Asset> CreateAsync(AssetInput input)
    {
        using var activity = FmlTelemetry.ActivitySource.StartActivity("AssetRegistry.Create");
        var asset = new Asset
        {
            Code = input.Code,
            Name = input.Name,
            AssetType = input.AssetType,
            Status = input.Status,
            Manufacturer = input.Manufacturer,
            Model = input.Model,
            HomePort = input.HomePort,
            CommissionedOn = DateTime.SpecifyKind(input.CommissionedOn, DateTimeKind.Utc),
            OperatingHours = input.OperatingHours,
            CapacityTonnes = input.CapacityTonnes,
            RatedPowerKw = input.RatedPowerKw,
        };
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();
        activity?.SetTag("fml.asset.code", asset.Code);
        return asset;
    }

    public async Task<Asset?> UpdateAsync(int id, AssetInput input)
    {
        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Id == id);
        if (asset is null)
        {
            return null;
        }

        asset.Code = input.Code;
        asset.Name = input.Name;
        asset.AssetType = input.AssetType;
        asset.Status = input.Status;
        asset.Manufacturer = input.Manufacturer;
        asset.Model = input.Model;
        asset.HomePort = input.HomePort;
        asset.CommissionedOn = DateTime.SpecifyKind(input.CommissionedOn, DateTimeKind.Utc);
        asset.OperatingHours = input.OperatingHours;
        asset.CapacityTonnes = input.CapacityTonnes;
        asset.RatedPowerKw = input.RatedPowerKw;
        await _db.SaveChangesAsync();
        return asset;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Id == id);
        if (asset is null)
        {
            return false;
        }

        _db.Assets.Remove(asset);
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>Called by MaintenanceScheduling when work starts or finishes on an asset.</summary>
    public async Task SetStatusAsync(int assetId, AssetStatus status)
    {
        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Id == assetId);
        if (asset is null)
        {
            return;
        }

        asset.Status = status;
        if (status == AssetStatus.InService)
        {
            asset.LastServicedUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }

    /// <summary>Assets past the preventive-maintenance interval, used by MaintenanceScheduling and Reporting.</summary>
    public Task<List<Asset>> GetAssetsDueForPreventiveMaintenanceAsync()
    {
        var interval = FmlConfig.PreventiveMaintenanceIntervalHours;
        var staleBefore = DateTime.UtcNow.AddDays(-60);
        return _db.Assets
            .Where(a => a.Status != AssetStatus.Retired
                        && a.OperatingHours >= interval
                        && (a.LastServicedUtc == null || a.LastServicedUtc < staleBefore))
            .OrderBy(a => a.Code)
            .ToListAsync();
    }
}
