using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components;

namespace OvcinaHra.Client.Auth;

/// <summary>
/// On API 401, refreshes the access token once and retries the request before
/// falling back to /login. Token-producing endpoints are bypassed to prevent
/// recursion.
/// </summary>
public class UnauthorizedRedirectHandler : DelegatingHandler
{
    private readonly NavigationManager _nav;
    private readonly IServiceProvider _services;

    public UnauthorizedRedirectHandler(NavigationManager nav, IServiceProvider services)
    {
        _nav = nav;
        _services = services;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        var isTokenEndpoint = path.EndsWith("/oidc-refresh", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith("/oidc-exchange", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith("/auth/refresh", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith("/dev-token", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith("/service-token", StringComparison.OrdinalIgnoreCase);

        if (request.Content is not null && !isTokenEndpoint)
            await request.Content.LoadIntoBufferAsync();

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized || isTokenEndpoint)
            return response;

        response.Dispose();

        var refresh = _services.GetRequiredService<TokenRefreshService>();
        await refresh.RefreshAsync();

        var auth = _services.GetRequiredService<JwtAuthStateProvider>();
        var newToken = await auth.GetTokenAsync();

        if (string.IsNullOrEmpty(newToken))
        {
            _nav.NavigateTo("/login", forceLoad: true);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        var retry = await CloneRequestAsync(request);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken);

        var retryResponse = await base.SendAsync(retry, cancellationToken);
        if (retryResponse.StatusCode == HttpStatusCode.Unauthorized)
            _nav.NavigateTo("/login", forceLoad: true);

        return retryResponse;
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version
        };

        if (original.Content is not null)
        {
            var ms = new MemoryStream();
            await original.Content.CopyToAsync(ms);
            ms.Position = 0;
            clone.Content = new StreamContent(ms);
            foreach (var h in original.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }

        foreach (var h in original.Headers)
            clone.Headers.TryAddWithoutValidation(h.Key, h.Value);

        return clone;
    }
}
