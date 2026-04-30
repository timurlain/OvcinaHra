namespace OvcinaHra.Api.Tests.ClientSmoke;

public class MapDoneOverlaySmokeTests
{
    [Fact]
    public void MapPage_WiresDoneLocationOverlay()
    {
        var root = FindRepoRoot();
        var mapPage = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Client",
            "Pages",
            "Map",
            "MapPage.razor"));
        var mapInterop = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Client",
            "wwwroot",
            "js",
            "map-interop.js"));
        var mapLayerRail = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Client",
            "Components",
            "MapLayerRail.razor"));
        var theme = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Client",
            "wwwroot",
            "css",
            "ovcinahra-theme.css"));

        Assert.Contains("Hotové lokace", mapLayerRail);
        Assert.Contains("ShowDoneLocations", mapLayerRail);
        Assert.Contains("OnDoneLocationsChange", mapLayerRail);
        Assert.Contains("DoneLocationStateKey", mapPage);
        Assert.Contains("[map-done] toggle changed", mapPage);
        Assert.Contains("l.IsPlacementDone", mapPage);
        Assert.Contains("showDoneBadge", mapInterop);
        Assert.Contains("bi-check-circle-fill", mapInterop);
        Assert.Contains("oh-map-pin-done", mapInterop);
        Assert.Contains("oh-map-pin-has-count", mapInterop);
        Assert.Contains(".oh-map-layer-done", theme);
        Assert.Contains(".oh-map-pin-done", theme);
        Assert.Contains(".oh-map-pin.oh-map-pin-has-count .oh-map-pin-done", theme);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OvcinaHra.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
