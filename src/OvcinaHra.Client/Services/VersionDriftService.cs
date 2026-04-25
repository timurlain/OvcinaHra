using Microsoft.Extensions.Logging;

namespace OvcinaHra.Client.Services;

/// <summary>
/// Polls <c>GET /api/version</c> every <see cref="PollInterval"/> and raises
/// <see cref="StateChanged"/> the first time the server-reported commit
/// differs from the one observed at app boot. Once drift is detected the
/// poll loop exits — the banner is a one-shot signal that lives until the
/// user reloads, so this service never flips <see cref="IsDrifted"/> back
/// to <c>false</c> (e.g. transient back-and-forth between two app instances
/// during a deploy must not silently dismiss the banner).
/// </summary>
public sealed class VersionDriftService : IDisposable
{
    /// <summary>60s — chatty enough to catch a deploy within a minute, quiet
    /// enough that a stale tab doesn't generate meaningful traffic. Bump to
    /// 120s if metrics show waste.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private readonly ApiClient _api;
    private readonly ILogger<VersionDriftService> _logger;
    private CancellationTokenSource? _cts;
    private bool _started;

    public event Action? StateChanged;

    public bool IsDrifted { get; private set; }
    public string? BaselineCommit { get; private set; }
    public string? LatestCommit { get; private set; }

    public VersionDriftService(ApiClient api, ILogger<VersionDriftService> logger)
    {
        _api = api;
        _logger = logger;
    }

    /// <summary>
    /// Idempotent — first call records the baseline commit and starts the
    /// poll loop. Subsequent calls are no-ops so the banner component can
    /// safely call this from <c>OnAfterRenderAsync(firstRender:true)</c>
    /// without race-arming the loop twice.
    /// </summary>
    public async Task StartAsync()
    {
        if (_started) return;
        _started = true;

        var first = await FetchCommitAsync();
        if (first is null) return;

        BaselineCommit = first;
        LatestCommit = first;

        _cts = new CancellationTokenSource();
        _ = PollLoopAsync(_cts.Token);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(PollInterval);
            while (!IsDrifted && await timer.WaitForNextTickAsync(ct))
            {
                var current = await FetchCommitAsync();
                if (current is null || current == BaselineCommit) continue;

                LatestCommit = current;
                IsDrifted = true;
                StateChanged?.Invoke();
                // Stop polling — drift is sticky until the user reloads.
                break;
            }
        }
        catch (OperationCanceledException)
        {
            // Component / app teardown.
        }
        catch (Exception ex)
        {
            // Defensive — the loop must never throw uncaught into Blazor's
            // dispatcher; that would tear down the SynchronizationContext.
            _logger.LogWarning(ex, "version drift poll loop crashed");
        }
    }

    private async Task<string?> FetchCommitAsync()
    {
        try
        {
            var info = await _api.GetAsync<VersionInfo>("/api/version");
            return info?.Commit;
        }
        catch (Exception ex)
        {
            // Drift polling is best-effort — a single failed fetch (network
            // hiccup, server restart mid-poll) must not surface to the user.
            _logger.LogDebug(ex, "version drift poll fetch failed (transient)");
            return null;
        }
    }

    private sealed record VersionInfo(string Commit, DateTimeOffset StartedUtc);

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
