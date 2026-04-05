using Microsoft.Playwright;

namespace OvcinaHra.E2E.Fixtures;

/// <summary>
/// Base class for E2E tests. Creates a fresh browser context per test class,
/// auto-logs in, and cleans the database.
/// </summary>
[Collection("E2E")]
public abstract class E2ETestBase : IAsyncLifetime
{
    protected readonly E2EFixture Fixture;
    protected IBrowserContext Context = null!;
    protected IPage Page = null!;

    protected E2ETestBase(E2EFixture fixture)
    {
        Fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await Fixture.CleanDatabaseAsync();

        Context = await Fixture.CreateContextAsync();
        Page = await Context.NewPageAsync();

        // Auto-login: get a dev token and inject into localStorage
        var token = await Fixture.GetDevTokenAsync();
        await Page.GotoAsync("/login");
        await Page.EvaluateAsync($"localStorage.setItem('auth_token', '{token}')");
        await Page.GotoAsync("/");
    }

    public async Task DisposeAsync()
    {
        await Page.CloseAsync();
        await Context.DisposeAsync();
    }
}
