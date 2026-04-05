using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Endpoints;

namespace OvcinaHra.Api.Tests.Fixtures;

[Collection("Postgres")]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected readonly PostgresFixture Postgres;
    protected ApiWebApplicationFactory Factory = null!;
    protected HttpClient Client = null!;

    protected IntegrationTestBase(PostgresFixture postgres)
    {
        Postgres = postgres;
    }

    public async Task InitializeAsync()
    {
        Factory = new ApiWebApplicationFactory(Postgres.ConnectionString);
        Client = Factory.CreateClient();

        // Get a dev token and set it on the client
        var tokenResponse = await Client.PostAsJsonAsync("/api/auth/dev-token",
            new DevTokenRequest("test-user", "test@ovcina.cz", "Test Organizátor"));
        var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.Token);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        await db.Database.MigrateAsync();

        // Clean all data between test classes for isolation
        var tableNames = db.Model.GetEntityTypes().Select(t => t.GetTableName()).Distinct().ToList();
        var truncateSql = string.Join("; ", tableNames.Select(t => $"TRUNCATE TABLE \"{t}\" CASCADE"));
        if (truncateSql.Length > 0)
            await db.Database.ExecuteSqlRawAsync(truncateSql);
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
    }
}
