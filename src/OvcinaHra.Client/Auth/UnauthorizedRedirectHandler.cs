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
    private const long MaxBufferedBodyBytes = 50L * 1024 * 1024;

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

        byte[]? bufferedBody = null;
        List<KeyValuePair<string, IEnumerable<string>>>? bufferedHeaders = null;

        if (request.Content is not null && !isTokenEndpoint)
        {
            var contentLength = request.Content.Headers.ContentLength;
            if (!contentLength.HasValue || contentLength.Value <= MaxBufferedBodyBytes)
            {
                bufferedBody = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                bufferedHeaders = request.Content.Headers
                    .Select(h => new KeyValuePair<string, IEnumerable<string>>(h.Key, h.Value))
                    .ToList();
            }
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized || isTokenEndpoint)
            return response;

        response.Dispose();

        var refresh = _services.GetRequiredService<TokenRefreshService>();
        await refresh.RefreshAsync(cancellationToken);

        var auth = _services.GetRequiredService<JwtAuthStateProvider>();
        var newToken = await auth.GetTokenAsync();

        if (string.IsNullOrEmpty(newToken))
        {
            _nav.NavigateTo("/login", forceLoad: true);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        using var retry = CloneRequest(request, bufferedBody, bufferedHeaders);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken);

        var retryResponse = await base.SendAsync(retry, cancellationToken);
        if (retryResponse.StatusCode == HttpStatusCode.Unauthorized)
            _nav.NavigateTo("/login", forceLoad: true);

        return retryResponse;
    }

    private static HttpRequestMessage CloneRequest(
        HttpRequestMessage original,
        byte[]? bufferedBody,
        List<KeyValuePair<string, IEnumerable<string>>>? bufferedHeaders)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version
        };

        if (bufferedBody is not null)
        {
            var content = new ByteArrayContent(bufferedBody);
            if (bufferedHeaders is not null)
            {
                foreach (var h in bufferedHeaders)
                    content.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
            clone.Content = content;
        }

        foreach (var h in original.Headers)
            clone.Headers.TryAddWithoutValidation(h.Key, h.Value);

        return clone;
    }
}
