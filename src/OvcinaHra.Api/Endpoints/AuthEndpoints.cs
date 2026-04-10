using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace OvcinaHra.Api.Endpoints;

public static class AuthEndpoints
{
    private const int AccessTokenMinutes = 60;
    private const int RefreshGraceDays = 7;

    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes, IConfiguration config, bool isDevelopment)
    {
        var group = routes.MapGroup("/api/auth").WithTags("Auth");

        if (isDevelopment)
        {
            // Dev-only: self-issued tokens for local dev and tests
            group.MapPost("/dev-token", (DevTokenRequest request) =>
            {
                var token = GenerateDevToken(config, [
                    new(ClaimTypes.NameIdentifier, request.UserId ?? "dev-user"),
                    new(ClaimTypes.Email, request.Email ?? "dev@ovcina.cz"),
                    new(ClaimTypes.Name, request.Name ?? "Dev Organizátor"),
                    new(ClaimTypes.Role, "Organizer")
                ]);
                return TypedResults.Ok(token);
            }).AllowAnonymous();

            // Dev-only: refresh self-issued tokens
            group.MapPost("/refresh", (HttpContext httpContext) =>
            {
                var authHeader = httpContext.Request.Headers.Authorization.ToString();
                if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    return Results.Unauthorized();

                var oldToken = authHeader["Bearer ".Length..];
                var principal = ValidateTokenForRefresh(config, oldToken);
                if (principal is null)
                    return Results.Unauthorized();

                var claims = principal.Claims
                    .Where(c => c.Type is not ("exp" or "iss" or "aud" or "nbf" or "iat" or "jti"))
                    .ToList();

                var token = GenerateDevToken(config, claims);
                return Results.Ok(token);
            }).AllowAnonymous();
        }

        // OIDC code exchange — WASM client sends the authorization code, API exchanges it for tokens
        var oidcAuthority = config["Oidc:Authority"];
        if (!string.IsNullOrEmpty(oidcAuthority))
        {
            group.MapPost("/oidc-exchange", async (OidcExchangeRequest request, IHttpClientFactory httpFactory) =>
            {
                var clientSecret = config["Oidc:ClientSecret"];
                if (string.IsNullOrEmpty(clientSecret))
                    return Results.Problem("Oidc:ClientSecret is not configured.", statusCode: 500);

                var client = httpFactory.CreateClient();
                var form = new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = request.Code,
                    ["redirect_uri"] = request.RedirectUri,
                    ["client_id"] = config["Oidc:ClientId"] ?? "ovcinahra",
                    ["client_secret"] = clientSecret,
                };
                if (!string.IsNullOrEmpty(request.CodeVerifier))
                    form["code_verifier"] = request.CodeVerifier;

                var tokenResponse = await client.PostAsync($"{oidcAuthority}/connect/token",
                    new FormUrlEncodedContent(form));

                if (!tokenResponse.IsSuccessStatusCode)
                    return Results.Unauthorized();

                var tokenData = await tokenResponse.Content.ReadFromJsonAsync<OidcTokenResponse>();
                if (tokenData?.AccessToken is null)
                    return Results.Unauthorized();

                return Results.Ok(new OidcExchangeResponse(tokenData.AccessToken, DateTime.UtcNow.AddSeconds(tokenData.ExpiresIn), tokenData.ExpiresIn, tokenData.RefreshToken));
            }).AllowAnonymous();

            group.MapPost("/oidc-refresh", async (OidcRefreshRequest request, IHttpClientFactory httpFactory) =>
            {
                var clientSecret = config["Oidc:ClientSecret"];
                if (string.IsNullOrEmpty(clientSecret))
                    return Results.Problem("Oidc:ClientSecret is not configured.", statusCode: 500);

                var client = httpFactory.CreateClient();
                var tokenResponse = await client.PostAsync($"{oidcAuthority}/connect/token",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "refresh_token",
                        ["refresh_token"] = request.RefreshToken,
                        ["client_id"] = config["Oidc:ClientId"] ?? "ovcinahra",
                        ["client_secret"] = clientSecret,
                    }));

                if (!tokenResponse.IsSuccessStatusCode)
                    return Results.Unauthorized();

                var tokenData = await tokenResponse.Content.ReadFromJsonAsync<OidcTokenResponse>();
                if (tokenData?.AccessToken is null)
                    return Results.Unauthorized();

                return Results.Ok(new TokenResponse(tokenData.AccessToken, DateTime.UtcNow.AddSeconds(tokenData.ExpiresIn), tokenData.ExpiresIn));
            }).AllowAnonymous();
        }

        // User info — works with both dev tokens and OIDC tokens
        group.MapGet("/me", (ClaimsPrincipal user) =>
        {
            if (user.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();

            // OIDC tokens use "sub", "name", "email", "role"
            // Dev tokens use ClaimTypes.NameIdentifier, ClaimTypes.Name, etc.
            var id = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
            var name = user.FindFirstValue("name") ?? user.FindFirstValue(ClaimTypes.Name);
            var email = user.FindFirstValue("email") ?? user.FindFirstValue(ClaimTypes.Email);
            var roles = user.FindAll("role").Select(c => c.Value)
                .Concat(user.FindAll(ClaimTypes.Role).Select(c => c.Value))
                .Distinct().ToList();

            return Results.Ok(new UserInfoDto(id ?? "", email ?? "", name ?? "", roles));
        }).RequireAuthorization();

        return group;
    }

    private static TokenResponse GenerateDevToken(IConfiguration config, List<Claim> claims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expires = DateTime.UtcNow.AddMinutes(AccessTokenMinutes);
        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: expires,
            signingCredentials: credentials);

        return new TokenResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            expires,
            AccessTokenMinutes * 60);
    }

    private static ClaimsPrincipal? ValidateTokenForRefresh(IConfiguration config, string token)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var handler = new JwtSecurityTokenHandler();

        try
        {
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = config["Jwt:Issuer"],
                ValidAudience = config["Jwt:Audience"],
                IssuerSigningKey = key,
                ValidateLifetime = false
            }, out var validatedToken);

            if (validatedToken is JwtSecurityToken jwt && jwt.ValidTo < DateTime.UtcNow.AddDays(-RefreshGraceDays))
                return null;

            return principal;
        }
        catch
        {
            return null;
        }
    }
}

public record DevTokenRequest(string? UserId = null, string? Email = null, string? Name = null);
public record TokenResponse(string Token, DateTime ExpiresUtc, int ExpiresInSeconds);
public record UserInfoDto(string UserId, string Email, string Name, List<string> Roles);

public record OidcExchangeRequest(string Code, string RedirectUri, string? CodeVerifier = null);
public record OidcRefreshRequest(string RefreshToken);
public record OidcExchangeResponse(string Token, DateTime ExpiresUtc, int ExpiresInSeconds, string? RefreshToken);

public record OidcTokenResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string? AccessToken,
    [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn,
    [property: System.Text.Json.Serialization.JsonPropertyName("token_type")] string? TokenType,
    [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")] string? RefreshToken);
