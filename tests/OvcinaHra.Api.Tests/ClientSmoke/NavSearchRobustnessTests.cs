namespace OvcinaHra.Api.Tests.ClientSmoke;

public class NavSearchRobustnessTests
{
    [Fact]
    public void NavSearch_LogsTransientErrors_AndPassesCancellationToApiClient()
    {
        var root = FindRepoRoot();
        var navSearch = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Client",
            "Layout",
            "NavSearch.razor"));
        var apiClient = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Client",
            "Services",
            "ApiClient.cs"));

        Assert.Contains("@inject ILogger<NavSearch> Logger", navSearch);
        Assert.Contains("Api.GetAsync<SearchResponseDto>(url, cancellationToken)", navSearch);
        Assert.Contains("catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)", navSearch);
        Assert.Contains("Logger.LogWarning(", navSearch);
        Assert.Contains("{Query}", navSearch);
        Assert.Contains("{StatusCode}", navSearch);
        Assert.Contains("{Exception}", navSearch);
        Assert.Contains("exception.GetType().Name", navSearch);

        Assert.Contains("GetAsync<T>(string url, CancellationToken cancellationToken = default)", apiClient);
        Assert.Contains("using var response = await _http.GetAsync(url, cancellationToken)", apiClient);
        Assert.Contains("ReadFromJsonAsync<T>(JsonOptions, cancellationToken)", apiClient);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OvcinaHra.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
