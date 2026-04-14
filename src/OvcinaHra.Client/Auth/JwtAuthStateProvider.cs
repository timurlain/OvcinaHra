using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace OvcinaHra.Client.Auth;

public class JwtAuthStateProvider : AuthenticationStateProvider
{
    private const string TokenKey = TokenRefreshService.AccessTokenKey;
    private const string RefreshTokenKey = TokenRefreshService.RefreshTokenKey;

    private readonly IJSRuntime _js;
    private readonly HttpClient _http;
    private readonly IServiceProvider _services;

    public JwtAuthStateProvider(IJSRuntime js, HttpClient http, IServiceProvider services)
    {
        _js = js;
        _http = http;
        _services = services;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await GetTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            _http.DefaultRequestHeaders.Authorization = null;
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Validate by calling the API — works with both dev JWTs and OIDC encrypted tokens.
        try
        {
            var response = await _http.GetAsync("/api/auth/me");
            if (response.IsSuccessStatusCode)
            {
                var me = await response.Content.ReadFromJsonAsync<MeResponse>();
                if (me is not null)
                {
                    var claims = new List<Claim>
                    {
                        new(ClaimTypes.NameIdentifier, me.UserId),
                        new(ClaimTypes.Name, me.Name),
                        new(ClaimTypes.Email, me.Email),
                    };
                    foreach (var role in me.Roles)
                        claims.Add(new(ClaimTypes.Role, role));

                    // Token is valid — make sure the refresh timer is running.
                    // Fire-and-forget; TryBootAsync is idempotent.
                    _ = _services.GetRequiredService<TokenRefreshService>().TryBootAsync();

                    return new AuthenticationState(
                        new ClaimsPrincipal(new ClaimsIdentity(claims, "oidc")));
                }
            }
        }
        catch
        {
            // API unreachable — treat as unauthenticated.
        }

        // Token invalid or API down — clear stale tokens from localStorage.
        await ClearTokenAsync();
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
    }

    public async Task<string?> GetTokenAsync()
    {
        try
        {
            return await _js.InvokeAsync<string?>("localStorage.getItem", TokenKey);
        }
        catch
        {
            return null;
        }
    }

    public async Task SetTokenAsync(string token)
    {
        await _js.InvokeVoidAsync("localStorage.setItem", TokenKey, token);
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task ClearTokenAsync()
    {
        await _js.InvokeVoidAsync("localStorage.removeItem", TokenKey);
        await _js.InvokeVoidAsync("localStorage.removeItem", RefreshTokenKey);
        _http.DefaultRequestHeaders.Authorization = null;
        _services.GetRequiredService<TokenRefreshService>().Stop();
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private record MeResponse(string UserId, string Email, string Name, List<string> Roles);
}
