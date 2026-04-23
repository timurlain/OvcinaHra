using OvcinaHra.Api.Data;
using OvcinaHra.Api.Endpoints;

namespace OvcinaHra.Api.Services;

/// <summary>
/// On container startup, walks every entity table with an image column and
/// pre-generates every <see cref="ThumbnailPreset"/> for every image-bearing
/// row. The thumbnail service short-circuits when a thumb already exists
/// (ExistsAsync HEAD probe), so re-running is idempotent and cheap after the
/// first pass.
///
/// Why this exists: on a fresh deploy the thumb cache is empty, so the first
/// visitor to a list page like /locations pays the cold resize cost for every
/// tile — that's minutes of wait and 503s from the Container App. Pre-gen
/// moves the cost off the read path.
///
/// <para>
/// <b>Does NOT block startup.</b> <see cref="StartAsync"/> schedules the
/// backfill on a background <see cref="Task"/> and returns immediately so the
/// container reports healthy without waiting. Respects the
/// <see cref="CancellationToken"/> passed to <see cref="StopAsync"/> so
/// shutdown isn't stalled by long-running resize work.
/// </para>
/// </summary>
public sealed class ThumbnailBackfillHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IThumbnailService _thumbnailService;
    private readonly ILogger<ThumbnailBackfillHostedService> _logger;
    private readonly CancellationTokenSource _stoppingCts = new();
    private Task? _backfillTask;

    public ThumbnailBackfillHostedService(
        IServiceScopeFactory scopeFactory,
        IThumbnailService thumbnailService,
        ILogger<ThumbnailBackfillHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _thumbnailService = thumbnailService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Thumbnail backfill hosted service scheduling background sweep.");
        // Run on a Task so StartAsync returns promptly — the container must
        // come up healthy even if backfill is still in flight.
        _backfillTask = Task.Run(() => RunBackfillAsync(_stoppingCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task RunBackfillAsync(CancellationToken ct)
    {
        try
        {
            // WorldDbContext is scoped — background services can't resolve
            // it from the root provider. Create a scope per sweep.
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            await ImageEndpoints.BackfillAllThumbsAsync(db, _thumbnailService, _logger, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Thumbnail backfill cancelled by shutdown.");
        }
        catch (Exception ex)
        {
            // Must not crash the host — swallow, log.
            _logger.LogWarning(ex, "Thumbnail backfill hosted service failed.");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stoppingCts.Cancel();

        if (_backfillTask is null)
            return;

        // Give the backfill a chance to wind down, but don't stall shutdown
        // past the host's stop timeout — whichever signal fires first wins.
        await Task.WhenAny(_backfillTask, Task.Delay(Timeout.Infinite, cancellationToken));
    }
}
