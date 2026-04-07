using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Client.Services;

/// <summary>
/// Holds the active game — the one with Status=Active in the database.
/// This is a global state, not a per-user selection. All per-game pages use this.
/// </summary>
public class GameContextService
{
    private readonly ApiClient _api;
    private int? _selectedGameId;
    private string? _gameName;
    private bool _initialized;

    public event Action? OnGameChanged;

    public GameContextService(ApiClient api) => _api = api;

    public int? SelectedGameId => _selectedGameId;
    public string? GameName => _gameName;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        try
        {
            var games = await _api.GetListAsync<GameListDto>("/api/games");
            var active = games.FirstOrDefault(g => g.Status == GameStatus.Active);
            if (active is not null)
            {
                _selectedGameId = active.Id;
                _gameName = $"{active.Name} (#{active.Edition})";
            }
        }
        catch { /* offline or API unavailable */ }
        _initialized = true;
    }

    /// <summary>
    /// Force a reload of the active game from the API.
    /// </summary>
    public async Task RefreshAsync()
    {
        _initialized = false;
        await InitializeAsync();
        OnGameChanged?.Invoke();
    }
}
