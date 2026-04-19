using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Timers;
using Microsoft.JSInterop;
using Timer = System.Timers.Timer;

namespace OvcinaHra.Client.Auth;

public record TokenResponse(string Token, DateTime ExpiresUtc, int ExpiresInSeconds);

public record OidcExchangeResponse(string Token, DateTime ExpiresUtc, int ExpiresInSeconds, string? RefreshToken);

internal record OidcRefreshRequestDto(string RefreshToken);

/// <summary>
/// Refreshes the access token before it expires, and re-arms itself after every reload.
/// Uses /api/auth/oidc-refresh when a refresh_token is stored (production OIDC path),
/// otherwise /api/auth/refresh (dev self-issued path).
/// </summary>
public class TokenRefreshService : IDisposable
{
    internal const string AccessTokenKey = "auth_token";
    internal const string RefreshTokenKey = "refresh_token";

    private readonly HttpClient _http;
    private readonly JwtAuthStateProvider _authProvider;
    private readonly IJSRuntime _js;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private Timer? _timer;
    private bool _booted;

    public TokenRefreshService(HttpClient http, JwtAuthStateProvider authProvider, IJSRuntime js)
    {
        _http = http;
        _authProvider = authProvider;
        _js = js;
    }

    /// <summary>
    /// Reads the stored access token, parses its <c>exp</c> claim, and schedules
    /// a refresh for 80% of the remaining lifetime. If the token is already
    /// expired or within 30 seconds of expiring, refreshes immediately.
    /// Idempotent — safe to call multiple times per app boot.
    /// </summary>
    public async Task TryBootAsync()
    {
        if (_booted) return;
        _booted = true;

        var token = await GetStoredAccessTokenAsync();
        if (string.IsNullOrEmpty(token)) return;

        var expiry = GetJwtExpiry(token);
        if (expiry is null)
        {
            // Unparseable token — try a refresh to see if we can recover.
            await RefreshAsync();
            return;
        }

        var remaining = expiry.Value - DateTimeOffset.UtcNow;
        if (remaining.TotalSeconds < 30)
        {
            await RefreshAsync();
            return;
        }

        ScheduleRefresh((int)remaining.TotalSeconds);
    }

    public void ScheduleRefresh(int expiresInSeconds)
    {
        _timer?.Dispose();

        // Refresh at 80% of remaining lifetime, minimum 10 seconds.
        var refreshMs = expiresInSeconds * 800;
        if (refreshMs < 10_000) refreshMs = 10_000;

        _timer = new Timer(refreshMs);
        _timer.Elapsed += async (_, _) => await RefreshAsync();
        _timer.AutoReset = false;
        _timer.Start();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Coalesce concurrent callers: if another refresh already produced a
            // fresh access token while we were queued, skip the round-trip.
            var currentToken = await GetStoredAccessTokenAsync();
            if (!string.IsNullOrEmpty(currentToken))
            {
                var expiry = GetJwtExpiry(currentToken);
                if (expiry.HasValue && (expiry.Value - DateTimeOffset.UtcNow).TotalSeconds > 60)
                    return;
            }

            var refreshToken = await GetStoredRefreshTokenAsync();

            if (!string.IsNullOrEmpty(refreshToken))
            {
                // Production OIDC path — stored refresh_token from registrace-ovcina.
                using var response = await _http.PostAsJsonAsync("/api/auth/oidc-refresh",
                    new OidcRefreshRequestDto(refreshToken), cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var token = await response.Content.ReadFromJsonAsync<OidcExchangeResponse>(cancellationToken);
                    if (token is not null)
                    {
                        await _authProvider.SetTokenAsync(token.Token);
                        if (!string.IsNullOrEmpty(token.RefreshToken))
                            await SetStoredRefreshTokenAsync(token.RefreshToken);
                        ScheduleRefresh(token.ExpiresInSeconds);
                        return;
                    }
                }

                if (IsExplicitAuthFailure(response.StatusCode))
                {
                    // Refresh token definitively rejected — clear it so we don't keep retrying.
                    await ClearStoredRefreshTokenAsync();
                    await _authProvider.ClearTokenAsync();
                    return;
                }

                // Transient (5xx, 408, 429, or null body on 2xx) — keep the tokens, retry shortly.
                ScheduleRetryAfterError();
                return;
            }

            // Dev path — self-issued token, no refresh_token involved.
            using var devResponse = await _http.PostAsync("/api/auth/refresh", null, cancellationToken);
            if (devResponse.IsSuccessStatusCode)
            {
                var token = await devResponse.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
                if (token is not null)
                {
                    await _authProvider.SetTokenAsync(token.Token);
                    ScheduleRefresh(token.ExpiresInSeconds);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Network error — retry shortly.
            ScheduleRetryAfterError();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static bool IsExplicitAuthFailure(HttpStatusCode status) =>
        status == HttpStatusCode.BadRequest
        || status == HttpStatusCode.Unauthorized
        || status == HttpStatusCode.Forbidden;

    private void ScheduleRetryAfterError()
    {
        _timer?.Dispose();
        _timer = new Timer(30_000);
        _timer.Elapsed += async (_, _) => await RefreshAsync();
        _timer.AutoReset = false;
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _booted = false;
    }

    // --- localStorage helpers ---

    private async Task<string?> GetStoredAccessTokenAsync()
    {
        try { return await _js.InvokeAsync<string?>("localStorage.getItem", AccessTokenKey); }
        catch { return null; }
    }

    private async Task<string?> GetStoredRefreshTokenAsync()
    {
        try { return await _js.InvokeAsync<string?>("localStorage.getItem", RefreshTokenKey); }
        catch { return null; }
    }

    private async Task SetStoredRefreshTokenAsync(string value)
    {
        try { await _js.InvokeVoidAsync("localStorage.setItem", RefreshTokenKey, value); }
        catch { }
    }

    private async Task ClearStoredRefreshTokenAsync()
    {
        try { await _js.InvokeVoidAsync("localStorage.removeItem", RefreshTokenKey); }
        catch { }
    }

    // --- JWT exp parsing (payload only, no signature check — client just needs the timestamp) ---

    private static DateTimeOffset? GetJwtExpiry(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return null;

            var payload = Base64UrlDecode(parts[1]);
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var unix))
                return DateTimeOffset.FromUnixTimeSeconds(unix);
        }
        catch { }
        return null;
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _refreshLock.Dispose();
    }
}
