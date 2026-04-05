using System.Net;
using OvcinaHra.Api.Tests.Fixtures;

namespace OvcinaHra.Api.Tests;

public class HealthCheckTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    [Fact]
    public async Task HealthCheck_ReturnsHealthy()
    {
        var response = await Client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
