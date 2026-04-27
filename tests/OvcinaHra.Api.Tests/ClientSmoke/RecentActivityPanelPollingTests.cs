namespace OvcinaHra.Api.Tests.ClientSmoke;

public class RecentActivityPanelPollingTests
{
    [Fact]
    public void RecentActivityPanel_SwallowsTransientPollErrors_AndKeepsCancellationPathSeparate()
    {
        var panelPath = Path.Combine(
            FindRepoRoot(),
            "src",
            "OvcinaHra.Client",
            "Components",
            "RecentActivityPanel.razor");

        var source = File.ReadAllText(panelPath);

        Assert.Contains("catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)", source);
        Assert.Contains("catch (Exception ex)", source);
        Assert.Contains("Logger.LogWarning(ex, \"Recent activity polling failed; retrying on the next tick.\");", source);
        Assert.Contains("Spojení přerušeno, obnovuji…", source);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OvcinaHra.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
