using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Endpoints;
using Testcontainers.PostgreSql;

namespace OvcinaHra.Api.Tests.Fixtures;

/// <summary>
/// Combined per-test-class fixture: provides an isolated Postgres database
/// AND a single shared <see cref="ApiWebApplicationFactory"/> for that class.
///
/// Why one fixture for both:
/// <list type="bullet">
///   <item>The WAF entry point can only be wrapped a bounded number of times
///         per process before its <c>HostFactoryResolver</c> trips with
///         "entry point exited without ever building an IHost". Sharing one
///         WAF per class avoids that — tens of factory instances across the
///         suite, not hundreds.</item>
///   <item>Per-class DBs (cloned from a migrated template) give parallelism
///         safety. Each class writes to a disjoint database.</item>
/// </list>
///
/// xUnit instantiates this fixture once per class via
/// <c>IClassFixture&lt;PostgresFixture&gt;</c>.
/// Tests within a class share the same WAF + HttpClient + DB and rely on a
/// per-test <c>TRUNCATE CASCADE</c> in <see cref="IntegrationTestBase"/>
/// to keep each test starting from the migrated baseline.
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private const string TemplateDbName = "ovcinahra_template";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    // WebApplicationFactory<TEntryPoint> uses process-static state in
    // HostFactoryResolver; bootstrapping multiple WAF instances concurrently
    // trips with "entry point exited without ever building an IHost". Each
    // class still gets its OWN WAF (and DB), but their construction is
    // serialized through this gate so the tests themselves run in parallel.
    private static readonly SemaphoreSlim WafGate = new(1, 1);
    private static PostgreSqlContainer? _sharedContainer;
    private static string? _adminConnectionString;

    private readonly string _instanceDbName =
        $"ovcinahra_test_{Guid.NewGuid().ToString("N")[..12]}";

    public string ConnectionString { get; private set; } = string.Empty;
    public ApiWebApplicationFactory Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await EnsureSharedAsync();

        // Clone the migrated template into a fresh per-class database.
        // CREATE DATABASE ... TEMPLATE is a fast file-system clone.
        await using (var conn = new NpgsqlConnection(_adminConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{_instanceDbName}\" TEMPLATE \"{TemplateDbName}\";";
            await cmd.ExecuteNonQueryAsync();
        }

        var b = new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = _instanceDbName };
        ConnectionString = b.ConnectionString;

        await WafGate.WaitAsync();
        try
        {
            Factory = new ApiWebApplicationFactory(ConnectionString);
            Client = Factory.CreateClient();
        }
        finally
        {
            WafGate.Release();
        }

        // Authenticate once per class — the dev-token is good for the
        // whole class lifetime, no need to re-auth per test.
        var tokenResponse = await Client.PostAsJsonAsync("/api/auth/dev-token",
            new DevTokenRequest("test-user", "test@ovcina.cz", "Test Organizátor"));
        var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.Token);
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (Factory is not null) await Factory.DisposeAsync();

        if (_adminConnectionString is null) return;

        // FORCE terminates pooled sessions — Postgres refuses DROP DATABASE
        // while sessions exist. WAF disposal above usually clears them, but
        // the FORCE clause is a belt-and-braces.
        await using var conn = new NpgsqlConnection(_adminConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_instanceDbName}\" WITH (FORCE);";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Truncates every model table and re-seeds the Kingdom lookup rows.
    /// Called from <see cref="IntegrationTestBase.InitializeAsync"/> per test
    /// so each test starts at the same migrated baseline.
    /// </summary>
    public async Task ResetDataAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();

        var tableNames = db.Model.GetEntityTypes().Select(t => t.GetTableName()).Distinct().ToList();
        var truncateSql = string.Join("; ", tableNames.Select(t => $"TRUNCATE TABLE \"{t}\" CASCADE"));
        if (truncateSql.Length > 0)
            await db.Database.ExecuteSqlRawAsync(truncateSql);

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Kingdoms" ("Name", "HexColor", "SortOrder") VALUES
              ('Aradhryand',      '#2E7D32', 1),
              ('Azanulinbar-Dum', '#C62828', 2),
              ('Esgaroth',        '#1565C0', 3),
              ('Nový Arnor',      '#F9A825', 4)
            """);
    }

    private static async Task EnsureSharedAsync()
    {
        if (_sharedContainer is not null) return;

        await Gate.WaitAsync();
        try
        {
            if (_sharedContainer is not null) return;

            var container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await container.StartAsync();

            // Testcontainers' default DB is a one-off; use the built-in
            // `postgres` admin database for CREATE/DROP DATABASE.
            var adminB = new NpgsqlConnectionStringBuilder(container.GetConnectionString())
            {
                Database = "postgres"
            };
            var adminConn = adminB.ConnectionString;

            await using (var conn = new NpgsqlConnection(adminConn))
            {
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"CREATE DATABASE \"{TemplateDbName}\";";
                await cmd.ExecuteNonQueryAsync();
            }

            // Migrate the template. Pooling=false ensures the connection truly
            // closes on dispose — CREATE DATABASE ... TEMPLATE refuses to clone
            // while any session is connected to the source.
            var migrationB = new NpgsqlConnectionStringBuilder(adminConn)
            {
                Database = TemplateDbName,
                Pooling = false
            };
            await using (var ctx = new WorldDbContext(
                new DbContextOptionsBuilder<WorldDbContext>()
                    .UseNpgsql(migrationB.ConnectionString)
                    .Options))
            {
                await ctx.Database.MigrateAsync();
            }

            _sharedContainer = container;
            _adminConnectionString = adminConn;
        }
        finally
        {
            Gate.Release();
        }
    }
}
