namespace OvcinaHra.Api.Tests.ClientSmoke;

public class ServiceWorkerFetchSmokeTests
{
    [Fact]
    public void PublishedServiceWorker_BypassesApiFetches_AndReturnsHttpFailureOnNetworkErrors()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "OvcinaHra.Client",
            "wwwroot",
            "service-worker.published.js"));

        Assert.Contains("shouldBypassOfflineCache(request)", source);
        Assert.Contains("url.pathname.startsWith('/api/')", source);
        Assert.Contains("return networkFetch(request);", source);
        Assert.Contains("async function networkFetch(request)", source);
        Assert.Contains("return await fetch(request);", source);
        Assert.Contains("status: 503", source);
        Assert.DoesNotContain("fetch(event.request)", source);

        var onFetchIndex = source.IndexOf("async function onFetch(event)", StringComparison.Ordinal);
        var bypassIndex = source.IndexOf("shouldBypassOfflineCache(request)", onFetchIndex, StringComparison.Ordinal);
        var cacheOpenIndex = source.IndexOf("caches.open(cacheName)", onFetchIndex, StringComparison.Ordinal);
        Assert.True(bypassIndex >= 0 && cacheOpenIndex > bypassIndex);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OvcinaHra.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
