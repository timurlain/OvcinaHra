namespace OvcinaHra.Api.Tests.ClientSmoke;

public class MapTextPopupSmokeTests
{
    [Fact]
    public void MapTextTool_UsesBlazorPopupInsteadOfBrowserPrompt()
    {
        var root = FindRepoRoot();
        var mapView = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Client",
            "Components",
            "MapView.razor"));
        var overlayInterop = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Client",
            "wwwroot",
            "js",
            "overlay-interop.js"));

        Assert.DoesNotContain("prompt(", overlayInterop);
        Assert.Contains("OnTextPlacementRequested", overlayInterop);
        Assert.Contains("HeaderText=\"Text na mapě\"", mapView);
        Assert.Contains("DxTextBox", mapView);
        Assert.Contains("[map-text-popup] open", mapView);
        Assert.Contains("[map-text-popup] submit", mapView);
        Assert.Contains("[map-text-popup] cancel", mapView);
        Assert.Contains("[map-text-popup] discard reason=empty", mapView);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OvcinaHra.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
