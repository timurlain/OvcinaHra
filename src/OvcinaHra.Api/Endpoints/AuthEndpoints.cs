using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.IdentityModel.Tokens;

namespace OvcinaHra.Api.Endpoints;

public static class AuthEndpoints
{
    private const int AccessTokenMinutes = 60;
    private const int RefreshGraceDays = 7;

    /// <summary>
    /// Dev-only token endpoint. In production, initial tokens come from registrace-ovčina.
    /// The /refresh endpoint works in all environments.
    /// </summary>
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes, IConfiguration config, bool isDevelopment)
    {
        var group = routes.MapGroup("/api/auth").WithTags("Auth");

        if (isDevelopment)
        {
            group.MapPost("/dev-token", (DevTokenRequest request) =>
            {
                var token = GenerateToken(config, [
                    new(ClaimTypes.NameIdentifier, request.UserId ?? "dev-user"),
                    new(ClaimTypes.Email, request.Email ?? "dev@ovcina.cz"),
                    new(ClaimTypes.Name, request.Name ?? "Dev Organizátor"),
                    new(ClaimTypes.Role, "Organizer")
                ]);
                return TypedResults.Ok(token);
            }).AllowAnonymous();
        }

        // Refresh — accepts a valid or recently-expired token, issues a fresh one.
        // Works in all environments. The client calls this before/after expiry.
        group.MapPost("/refresh", (HttpContext httpContext) =>
        {
            var authHeader = httpContext.Request.Headers.Authorization.ToString();
            if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return Results.Unauthorized();

            var oldToken = authHeader["Bearer ".Length..];
            var principal = ValidateTokenForRefresh(config, oldToken);
            if (principal is null)
                return Results.Unauthorized();

            // Carry over identity claims, issue a fresh token
            var claims = new List<Claim>();
            foreach (var claim in principal.Claims)
            {
                // Skip JWT-internal claims (exp, iss, aud, etc.)
                if (claim.Type is "exp" or "iss" or "aud" or "nbf" or "iat" or "jti")
                    continue;
                claims.Add(new Claim(claim.Type, claim.Value));
            }

            var token = GenerateToken(config, claims);
            return Results.Ok(token);
        }).AllowAnonymous(); // Must be anonymous — the old token may be expired

        group.MapGet("/me", (ClaimsPrincipal user) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            var email = user.FindFirstValue(ClaimTypes.Email);
            var name = user.FindFirstValue(ClaimTypes.Name);
            return TypedResults.Ok(new UserInfoDto(userId!, email!, name!));
        }).RequireAuthorization();

        return group;
    }

    private static TokenResponse GenerateToken(IConfiguration config, List<Claim> claims)
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

    /// <summary>
    /// Validates a token for refresh purposes — accepts recently expired tokens
    /// (within RefreshGraceDays) so the user doesn't lose their session.
    /// </summary>
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
                // Allow expired tokens within the grace period
                ValidateLifetime = false
            }, out var validatedToken);

            // Check the token hasn't expired beyond the grace period
            if (validatedToken is JwtSecurityToken jwt)
            {
                var expiry = jwt.ValidTo;
                if (expiry < DateTime.UtcNow.AddDays(-RefreshGraceDays))
                    return null; // Too old to refresh
            }

            return principal;
        }
        catch
        {
            return null; // Invalid signature, malformed, etc.
        }
    }
}

public record DevTokenRequest(string? UserId = null, string? Email = null, string? Name = null);
public record TokenResponse(string Token, DateTime ExpiresUtc, int ExpiresInSeconds);
public record UserInfoDto(string UserId, string Email, string Name);
