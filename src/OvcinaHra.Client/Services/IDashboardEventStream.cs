using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Client.Services;

public interface IDashboardEventStream
{
    Task<IReadOnlyList<DashboardRecentEventDto>> GetRecentEventsAsync(
        int gameId,
        DateTime? sinceUtc,
        CancellationToken cancellationToken = default);
}
