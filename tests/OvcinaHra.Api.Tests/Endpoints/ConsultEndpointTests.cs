using System.Net;
using System.Net.Http.Json;
using OvcinaHra.Api.Tests.Fixtures;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Endpoints;

public class ConsultEndpointTests(PostgresFixture postgres)
    : IntegrationTestBase(postgres), IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Availability_WithoutBotConfig_ReturnsDisabled()
    {
        var availability = await Client.GetFromJsonAsync<ConsultAvailabilityDto>(
            "/api/consult/available");

        Assert.NotNull(availability);
        Assert.False(availability.Enabled);
    }

    [Fact]
    public async Task Availability_WithoutToken_ReturnsUnauthorized()
    {
        using var noAuthClient = Factory.CreateClient();

        var response = await noAuthClient.GetAsync("/api/consult/available");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ask_WithoutBotConfig_ReturnsUnavailable()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/consult/loremaster",
            new ConsultRequestDto("Kde je Drozd?"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Reset_WithoutBotConfig_ReturnsUnavailable()
    {
        var response = await Client.PostAsync("/api/consult/rulemaster/reset", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
