using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace OvcinaHra.Client.Auth;

public class JwtAuthStateProvider : AuthenticationStateProvider
{
    private const string TokenKey = "auth_token";
    private readonly IJSRuntime _js;
    private readonly HttpClient _http;

    public JwtAuthStateProvider(IJSRuntime js, HttpClient http)
    {
        _js = js;
        _http = http;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await GetTokenAsync();
        if (string.IsNullOrEmpty(token))
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));

        var identity = ParseToken(token);
        if (identity is null)
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));

        return new AuthenticationState(new ClaimsPrincipal(identity));
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
        _http.DefaultRequestHeaders.Authorization = null;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private static ClaimsIdentity? ParseToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);

            if (jwt.ValidTo < DateTime.UtcNow)
                return null;

            return new ClaimsIdentity(jwt.Claims, "jwt");
        }
        catch
        {
            return null;
        }
    }
}
