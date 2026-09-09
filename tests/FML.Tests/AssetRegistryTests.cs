using FML.Api.AssetRegistry;

namespace FML.Tests;

public class AssetRegistryTests
{
    [Fact]
    public async Task Create_persists_asset_with_defaults()
    {
        using var ctx = new FmlTestContext();

        var asset = await ctx.CreateAssetAsync("ENG-900", operatingHours: 1234);

        Assert.True(asset.Id > 0);
        Assert.Equal("ENG-900", asset.Code);
        Assert.Equal(AssetStatus.InService, asset.Status);
        Assert.Equal(1234, asset.OperatingHours);
        Assert.NotNull(await ctx.Assets.GetByCodeAsync("ENG-900"));
    }

    [Fact]
    public async Task Update_changes_mutable_fields()
    {
        using var ctx = new FmlTestContext();
        var asset = await ctx.CreateAssetAsync("ENG-901");

        var updated = await ctx.Assets.UpdateAsync(asset.Id, new AssetInput(
            "ENG-901",
            "Renamed engine",
            AssetType.Engine,
            AssetStatus.Standby,
            "MAN",
            "6L32",
            "Gdansk",
            asset.CommissionedOn,
            2222,
            0,
            3480));

        Assert.NotNull(updated);
        Assert.Equal("Renamed engine", updated!.Name);
        Assert.Equal(AssetStatus.Standby, updated.Status);
        Assert.Equal("Gdansk", updated.HomePort);
        Assert.Equal(2222, updated.OperatingHours);
    }

    [Fact]
    public async Task Delete_removes_asset_and_missing_ids_are_reported()
    {
        using var ctx = new FmlTestContext();
        var asset = await ctx.CreateAssetAsync("ENG-902");

        Assert.True(await ctx.Assets.DeleteAsync(asset.Id));
        Assert.Null(await ctx.Assets.GetAsync(asset.Id));
        Assert.False(await ctx.Assets.DeleteAsync(asset.Id));
    }

    [Fact]
    public async Task List_filters_by_status_and_type()
    {
        using var ctx = new FmlTestContext();
        var inService = await ctx.CreateAssetAsync("ENG-903");
        var standby = await ctx.CreateAssetAsync("ENG-904");
        await ctx.Assets.SetStatusAsync(standby.Id, AssetStatus.Standby);

        var all = await ctx.Assets.GetAllAsync();
        var onlyInService = await ctx.Assets.GetAllAsync(status: AssetStatus.InService);
        var cranes = await ctx.Assets.GetAllAsync(type: AssetType.Crane);

        Assert.Equal(2, all.Count);
        Assert.Equal(new[] { inService.Id }, onlyInService.Select(a => a.Id));
        Assert.Empty(cranes);
    }

    [Fact]
    public async Task Assets_past_the_preventive_interval_are_flagged()
    {
        using var ctx = new FmlTestContext();
        await ctx.CreateAssetAsync("ENG-905", operatingHours: 500);
        var due = await ctx.CreateAssetAsync("ENG-906", operatingHours: 9000);

        var flagged = await ctx.Assets.GetAssetsDueForPreventiveMaintenanceAsync();

        Assert.Equal(new[] { due.Id }, flagged.Select(a => a.Id));
    }
}
