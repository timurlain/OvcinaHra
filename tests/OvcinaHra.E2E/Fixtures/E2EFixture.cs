using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using OvcinaHra.Api.Data;
using Testcontainers.PostgreSql;

namespace OvcinaHra.E2E.Fixtures;

/// <summary>
/// Shared fixture: starts PostgreSQL container, API server, and Playwright browser.
/// API-level E2E tests use ApiClient directly.
/// Browser E2E tests use Playwright against the running server.
/// </summary>
public class E2EFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private IPlaywright _playwright = null!;

    public IBrowser Browser { get; private set; } = null!;
    public string BaseUrl { get; private set; } = null!;
    public HttpClient ApiClient { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<WorldDbContext>));
                if (descriptor != null) services.Remove(descriptor);
                services.AddDbContext<WorldDbContext>(options =>
                    options.UseNpgsql(_postgres.GetConnectionString()));
            });
        });

        ApiClient = _factory.CreateClient();
        BaseUrl = _factory.Server.BaseAddress.ToString().TrimEnd('/');

        // Migrate database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        await db.Database.MigrateAsync();

        // Playwright
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    public async Task<IBrowserContext> CreateContextAsync()
    {
        return await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl
        });
    }

    public async Task<string> GetDevTokenAsync(string name = "E2E Tester", string email = "e2e@ovcina.cz")
    {
        var response = await ApiClient.PostAsJsonAsync("/api/auth/dev-token",
            new { Name = name, Email = email });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return json!["token"].ToString()!;
    }

    public async Task CleanDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        var tableNames = db.Model.GetEntityTypes().Select(t => t.GetTableName()).Distinct().ToList();
        var truncateSql = string.Join("; ", tableNames.Select(t => $"TRUNCATE TABLE \"{t}\" CASCADE"));
        if (truncateSql.Length > 0)
            await db.Database.ExecuteSqlRawAsync(truncateSql);
    }

    public async Task DisposeAsync()
    {
        ApiClient.Dispose();
        await Browser.DisposeAsync();
        _playwright.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition("E2E")]
public class E2ECollection : ICollectionFixture<E2EFixture>;
