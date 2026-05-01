namespace OvcinaHra.Api.Tests.ClientSmoke;

/// <summary>
/// Lightweight content-assertion smoke tests for the Rozpoznat skrýš client surface.
/// Mirrors the existing ClientSmoke pattern (e.g. BotConsultDrawerSmokeTests):
/// no bUnit harness — just verify the rendered Razor source contains the contract bits
/// (route, auth, no-game guard, modal wiring, NavMenu link, tile entry).
/// </summary>
public class RecognizeStashSmokeTests
{
    [Fact]
    public void Page_HasAuthorizeAttribute_AndGameRequiredGuard()
    {
        var root = FindRepoRoot();
        var page = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Client",
            "Pages",
            "RecognizeStash",
            "RecognizeStashPage.razor"));

        Assert.Contains("@page \"/recognize-stash\"", page);
        Assert.Contains("@attribute [Authorize]", page);
        Assert.Contains("GameContext.SelectedGameId is null", page);
        Assert.Contains("Zatím není vybrána žádná hra", page);
        Assert.Contains("Otevřít Správu her", page);
    }

    [Fact]
    public void Page_RendersCameraCapture_AndRecognizeButton_AndModal()
    {
        var root = FindRepoRoot();
        var page = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Client",
            "Pages",
            "RecognizeStash",
            "RecognizeStashPage.razor"));

        Assert.Contains("<CameraCapture OnCaptured=\"OnPhotoCaptured\"", page);
        Assert.Contains("Vyfotit znovu", page);
        Assert.Contains(">Rozpoznat<", page);
        Assert.Contains("Hledám shodu", page);
        Assert.Contains("<StampMatchModal", page);
        Assert.Contains("Visible=\"_modalVisible\"", page);
        Assert.Contains("Result=\"_result\"", page);
        Assert.Contains("OnClose=\"CloseModal\"", page);
    }

    [Fact]
    public void Page_PostsToRecognizeEndpoint_WithGameIdInBody()
    {
        var root = FindRepoRoot();
        var page = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Client",
            "Pages",
            "RecognizeStash",
            "RecognizeStashPage.razor"));

        Assert.Contains("\"/api/stamps/recognize\"", page);
        Assert.Contains("RecognizeStashRequest(gameId, _capturedDataUrl)", page);
        Assert.Contains("PostAsync<RecognizeStashRequest, RecognizeStashResponse>", page);
        // Errors must be Czech, not exception-string-leaks.
        Assert.Contains("LLM rozpoznání selhalo", page);
        Assert.Contains("LLM rozpoznání je dočasně omezené", page);
        Assert.Contains("LLM rozpoznání není v tomto prostředí nakonfigurované", page);
    }

    [Fact]
    public void Modal_RendersTopCandidatesWithStashes_AndHandlesEmptyAndNoReferenceStates()
    {
        var root = FindRepoRoot();
        var modal = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Client",
            "Components",
            "StampMatchModal.razor"));

        Assert.Contains("Rozpoznané skrýše", modal);
        Assert.Contains("Tato hra nemá žádné lokace s razítkem", modal);
        Assert.Contains("Snímek se nepodařilo přiřadit k žádné lokaci", modal);
        Assert.Contains("Result.Candidates", modal);
        Assert.Contains("candidate.Stashes", modal);
        Assert.Contains("Zavřít", modal);
        Assert.Contains("OnClose.InvokeAsync()", modal);
        // Confidence threshold colours must be present — green/yellow/grey buckets.
        Assert.Contains("bg-success", modal);
        Assert.Contains("bg-warning", modal);
        Assert.Contains("bg-secondary", modal);
    }

    [Fact]
    public void QuickLaunchTiles_ContainsRecognizeStashTile()
    {
        var root = FindRepoRoot();
        var tiles = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Client",
            "Components",
            "QuickLaunchTiles.razor"));

        Assert.Contains("\"recognize\"", tiles);
        Assert.Contains("Rozpoznat skrýš", tiles);
        Assert.Contains("/recognize-stash", tiles);
        Assert.Contains("bi-search", tiles);
        // Layout must drop the --single modifier once we have multiple tiles.
        Assert.DoesNotContain("oh-home-qlaunch-grid--single", tiles);
    }

    [Fact]
    public void NavMenu_HasRecognizeStashLink_UnderHraMode()
    {
        var root = FindRepoRoot();
        var nav = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Client",
            "Layout",
            "NavMenu.razor"));

        Assert.Contains("href=\"recognize-stash\"", nav);
        Assert.Contains(">Rozpoznat skrýš<", nav);

        // The link must sit inside the @if (mode == "hra") block (game-scoped items)
        // and NOT in the catalog branch. We assert it lives before the catalog-only
        // anchor "locations?catalog=true" which is the first item in the else branch.
        var recognizeIdx = nav.IndexOf("href=\"recognize-stash\"", StringComparison.Ordinal);
        var catalogStart = nav.IndexOf("locations?catalog=true", StringComparison.Ordinal);
        Assert.True(recognizeIdx >= 0, "Expected the recognize-stash NavLink in NavMenu.razor");
        Assert.True(catalogStart > recognizeIdx,
            "Recognize-stash NavLink must be inside the hra-mode block, not the catalog branch.");
    }

    private static string FindRepoRoot()
    {
        // Walk up from the test binary until we find the .slnx — same pattern the other
        // ClientSmoke tests use (no shared helper to import without enlarging surface).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OvcinaHra.slnx")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
