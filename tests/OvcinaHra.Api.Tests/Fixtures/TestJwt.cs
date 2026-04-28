using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace OvcinaHra.Api.Tests.Fixtures;

internal static class TestJwt
{
    public static string CreateToken(IServiceProvider services, params string[] roles)
    {
        var config = services.GetRequiredService<IConfiguration>();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "test-non-organizer"),
            new(ClaimTypes.NameIdentifier, "test-non-organizer"),
            new(ClaimTypes.Email, "player@example.test"),
            new("name", "Test Player")
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
