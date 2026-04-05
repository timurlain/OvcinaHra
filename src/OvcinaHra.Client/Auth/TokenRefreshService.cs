using System.Net.Http.Json;
using System.Timers;
using Timer = System.Timers.Timer;

namespace OvcinaHra.Client.Auth;

public record TokenResponse(string Token, DateTime ExpiresUtc, int ExpiresInSeconds);

/// <summary>
/// Automatically refreshes the JWT token before it expires.
/// Runs a timer that fires at 80% of token lifetime.
/// </summary>
public class TokenRefreshService : IDisposable
{
    private readonly HttpClient _http;
    private readonly JwtAuthStateProvider _authProvider;
    private Timer? _timer;

    public TokenRefreshService(HttpClient http, JwtAuthStateProvider authProvider)
    {
        _http = http;
        _authProvider = authProvider;
    }

    public void ScheduleRefresh(int expiresInSeconds)
    {
        _timer?.Dispose();

        // Refresh at 80% of token lifetime
        var refreshMs = expiresInSeconds * 800;
        if (refreshMs < 10_000) refreshMs = 10_000; // At least 10 seconds

        _timer = new Timer(refreshMs);
        _timer.Elapsed += async (_, _) => await RefreshAsync();
        _timer.AutoReset = false;
        _timer.Start();
    }

    public async Task RefreshAsync()
    {
        try
        {
            var response = await _http.PostAsync("/api/auth/refresh", null);
            if (response.IsSuccessStatusCode)
            {
                var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
                if (token is not null)
                {
                    await _authProvider.SetTokenAsync(token.Token);
                    ScheduleRefresh(token.ExpiresInSeconds);
                }
            }
        }
        catch
        {
            // Network error — retry in 30 seconds
            _timer?.Dispose();
            _timer = new Timer(30_000);
            _timer.Elapsed += async (_, _) => await RefreshAsync();
            _timer.AutoReset = false;
            _timer.Start();
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
