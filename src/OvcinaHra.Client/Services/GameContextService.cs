using Microsoft.JSInterop;

namespace OvcinaHra.Client.Services;

/// <summary>
/// Holds the currently selected game edition. All per-game pages use this
/// instead of hardcoding a game ID. Persisted to localStorage.
/// </summary>
public class GameContextService
{
    private const string StorageKey = "selected_game_id";
    private readonly IJSRuntime _js;
    private int? _selectedGameId;
    private bool _initialized;

    public event Action? OnGameChanged;

    public GameContextService(IJSRuntime js) => _js = js;

    public int? SelectedGameId => _selectedGameId;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        try
        {
            var stored = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (int.TryParse(stored, out var id))
                _selectedGameId = id;
        }
        catch { /* pre-render */ }
        _initialized = true;
    }

    public async Task SetGameAsync(int? gameId)
    {
        _selectedGameId = gameId;
        if (gameId.HasValue)
            await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, gameId.Value.ToString());
        else
            await _js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        OnGameChanged?.Invoke();
    }
}
