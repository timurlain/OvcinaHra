using System.Net;
using Microsoft.AspNetCore.Components;

namespace OvcinaHra.Client.Auth;

public class UnauthorizedRedirectHandler : DelegatingHandler
{
    private readonly NavigationManager _nav;

    public UnauthorizedRedirectHandler(NavigationManager nav)
    {
        _nav = nav;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        // Don't redirect for auth endpoints — let them handle 401 internally
        var path = request.RequestUri?.AbsolutePath ?? "";
        if (response.StatusCode == HttpStatusCode.Unauthorized && !path.Contains("/auth/"))
        {
            _nav.NavigateTo("/login", forceLoad: true);
        }

        return response;
    }
}
