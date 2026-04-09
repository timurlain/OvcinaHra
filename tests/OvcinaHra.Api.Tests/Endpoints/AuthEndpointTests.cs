using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using OvcinaHra.Api.Endpoints;
using OvcinaHra.Api.Tests.Fixtures;

namespace OvcinaHra.Api.Tests.Endpoints;

public class AuthEndpointTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    [Fact]
    public async Task DevToken_ReturnsValidToken()
    {
        // Client already has a token from InitializeAsync, but test explicitly
        var noAuthClient = Factory.CreateClient();
        var response = await noAuthClient.PostAsJsonAsync("/api/auth/dev-token",
            new DevTokenRequest("auth-test", "auth@ovcina.cz", "Auth Tester"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(token);
        Assert.False(string.IsNullOrEmpty(token.Token));
        Assert.True(token.ExpiresInSeconds > 0);
    }

    [Fact]
    public async Task Me_WithValidToken_ReturnsUserInfo()
    {
        var response = await Client.GetFromJsonAsync<UserInfoDto>("/api/auth/me");
        Assert.NotNull(response);
        Assert.Equal("test-user", response.UserId);
        Assert.Equal("test@ovcina.cz", response.Email);
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        var noAuthClient = Factory.CreateClient();
        var response = await noAuthClient.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CrudEndpoint_WithoutToken_Returns401()
    {
        var noAuthClient = Factory.CreateClient();
        var response = await noAuthClient.GetAsync("/api/games");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithValidToken_ReturnsNewToken()
    {
        var response = await Client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var newToken = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(newToken);
        Assert.False(string.IsNullOrEmpty(newToken.Token));
    }

    [Fact]
    public async Task Refresh_WithoutToken_Returns401()
    {
        var noAuthClient = Factory.CreateClient();
        var response = await noAuthClient.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HealthCheck_WithoutToken_ReturnsOk()
    {
        var noAuthClient = Factory.CreateClient();
        var response = await noAuthClient.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Me_ReturnsRoles()
    {
        var response = await Client.GetFromJsonAsync<UserInfoDto>("/api/auth/me");
        Assert.NotNull(response);
        Assert.Contains("Organizer", response.Roles);
    }
}
