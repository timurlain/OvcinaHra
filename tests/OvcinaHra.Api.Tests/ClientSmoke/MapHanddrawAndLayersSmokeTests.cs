namespace OvcinaHra.Api.Tests.ClientSmoke;

public class MapHanddrawAndLayersSmokeTests
{
    [Fact]
    public void OverlayInterop_SmoothsAndJoinsFreehandStrokes()
    {
        var root = FindRepoRoot();
        var overlayInterop = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Client",
            "wwwroot",
            "js",
            "overlay-interop.js"));
        var mapView = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Client",
            "Components",
            "MapView.razor"));

        Assert.Contains("FREEHAND_SIMPLIFY_EPSILON_PX", overlayInterop);
        Assert.Contains("simplifyProjectedPoints", overlayInterop);
        Assert.Contains("catmullRomSpline", overlayInterop);
        Assert.Contains("maybeJoinFreehandShape", overlayInterop);
        Assert.Contains("[map-handdraw]", overlayInterop);
        Assert.Contains("smoothing.input", overlayInterop);
        Assert.Contains("smoothing.simplified", overlayInterop);
        Assert.Contains("smoothing.splined", overlayInterop);
        Assert.Contains("edge-join.merged", overlayInterop);
        Assert.Contains("[map-handdraw] shape-replaced", mapView);
    }

    [Fact]
    public void MapPage_UsesCompactLayersControlForOverlayToggles()
    {
        var root = FindRepoRoot();
        var mapPage = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Client",
            "Pages",
            "Map",
            "MapPage.razor"));
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

        Assert.Contains("oh-map-layers-button", mapLayerRail);
        Assert.Contains("DxFlyout", mapLayerRail);
        Assert.Contains("[map-layers] layers-control", mapLayerRail);
        Assert.Contains("[map-layers] layer-toggle changed", mapLayerRail);
        Assert.Contains("Skrýše", mapLayerRail);
        Assert.Contains("Zobrazit oblasti", mapLayerRail);
        Assert.Contains("Hotové lokace", mapLayerRail);
        Assert.DoesNotContain("oh-map-rail-toggle", mapPage);
        Assert.DoesNotContain("oh-map-region-toggle", mapPage);
        Assert.Contains(".oh-map-layers-control", theme);
        Assert.Contains(".oh-map-layers-panel", theme);
        Assert.Contains("left:102px", theme);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OvcinaHra.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
