namespace OvcinaHra.Api.Tests.ClientSmoke;

public class RegistraceDiagnosticLoggingTests
{
    [Fact]
    public void RegistraceLinkFlow_HasDiagnosticLogsAcrossPopupProxyAndUpstream()
    {
        var root = FindRepoRoot();
        var popup = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Client",
            "Pages",
            "Games",
            "LinkToRegistracePopup.razor"));
        var endpoints = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Api",
            "Endpoints",
            "GameEndpoints.cs"));
        var service = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Api",
            "Services",
            "RegistraceGameService.cs"));

        Assert.Contains("[registrace] popup opened gameId={GameId}", popup);
        Assert.Contains("[registrace] fetching games for game {GameId}", popup);
        Assert.Contains("[registrace] calling Api.GetListAsync<RegistraceGameDto>", popup);
        Assert.Contains("[registrace] response received, count={fetched.Count}", popup);
        Assert.Contains("[registrace] parsed {fetched.Count} games", popup);
        Assert.Contains("[registrace] state updated, rendering grid", popup);
        Assert.Contains("[registrace] caught {ex.GetType().Name}: {ex.Message}", popup);
        Assert.Contains("[registrace] state cleanup, loading={loading}", popup);
        Assert.Contains("Api.GetListAsync<RegistraceGameDto>(url)", popup);
        Assert.DoesNotContain("Http.GetAsync", popup);

        Assert.Contains("[registrace] proxy entry from userId={UserId} localGameId={LocalGameId}", endpoints);
        Assert.Contains("[registrace] proxy calling RegistraceGameService.GetAvailableAsync", endpoints);
        Assert.Contains("[registrace] proxy upstream returned {Count} games in {ElapsedMs}ms", endpoints);
        Assert.Contains("[registrace] proxy filtered linkedCount={LinkedCount} returnedCount={ReturnedCount}", endpoints);
        Assert.Contains("[registrace] proxy upstream timeout", endpoints);
        Assert.Contains("[registrace] proxy upstream HTTP exception", endpoints);
        Assert.Contains("[registrace] proxy unexpected exception", endpoints);

        Assert.Contains("[registrace] upstream request prepared", service);
        Assert.Contains("[registrace] upstream {Endpoint} completed in {ElapsedMs} ms", service);
        Assert.Contains("\"cancelled\"", service);
        Assert.Contains("[registrace] upstream response body read", service);
        Assert.Contains("[registrace] upstream parsed {Count} games", service);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OvcinaHra.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
