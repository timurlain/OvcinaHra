using System.Globalization;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Client.Services;

public sealed class PollingDashboardEventStream(ApiClient api) : IDashboardEventStream
{
    public async Task<IReadOnlyList<DashboardRecentEventDto>> GetRecentEventsAsync(
        int gameId,
        DateTime? sinceUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var url = $"/api/dashboard/events/recent?gameId={gameId}";
        if (sinceUtc is not null)
        {
            var since = sinceUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            url += $"&since={Uri.EscapeDataString(since)}";
        }

        var rows = await api.GetListAsync<DashboardRecentEventDto>(url);
        cancellationToken.ThrowIfCancellationRequested();
        return rows;
    }
}
