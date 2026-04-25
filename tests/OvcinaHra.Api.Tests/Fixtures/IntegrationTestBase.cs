namespace OvcinaHra.Api.Tests.Fixtures;

/// <summary>
/// Per-test reset on a per-class fixture.
///
/// <see cref="PostgresFixture"/> owns the database, WAF, and authenticated
/// HttpClient for the whole test class. This base class plugs into xUnit's
/// per-test lifecycle to TRUNCATE + re-seed before each test, so test order
/// inside a class doesn't matter.
///
/// Test classes look like:
/// <code>
/// public class FooTests(PostgresFixture postgres)
///     : IntegrationTestBase(postgres), IClassFixture&lt;PostgresFixture&gt; { ... }
/// </code>
/// </summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected readonly PostgresFixture Postgres;

    protected ApiWebApplicationFactory Factory => Postgres.Factory;
    protected HttpClient Client => Postgres.Client;

    protected IntegrationTestBase(PostgresFixture postgres)
    {
        Postgres = postgres;
    }

    public Task InitializeAsync() => Postgres.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}
