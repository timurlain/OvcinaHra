using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;

namespace OvcinaHra.Api.Services;

public sealed class EventIdempotencyCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<EventIdempotencyCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(12);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CleanupOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(CleanupInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CleanupOnceAsync(stoppingToken);
        }
    }

    public static Task<int> CleanupAsync(
        WorldDbContext db,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var cutoff = utcNow.AddDays(-7);
        return db.EventIdempotencies
            .Where(e => e.CreatedAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task CleanupOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            var deleted = await CleanupAsync(db, DateTime.UtcNow, cancellationToken);
            if (deleted > 0)
                logger.LogInformation("Deleted {Count} expired event idempotency keys.", deleted);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Event idempotency cleanup failed.");
        }
    }
}
