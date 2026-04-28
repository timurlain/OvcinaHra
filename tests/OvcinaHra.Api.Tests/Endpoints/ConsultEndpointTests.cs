using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OvcinaHra.Api.Endpoints;
using OvcinaHra.Api.Services;
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

    [Fact]
    public async Task Ask_UpstreamFailure_UsesFixedCzechDetail_AndLogsRawDetail()
    {
        const string upstreamDetail = "Traceback: SECRET bot stack trace";
        const string userDetail = "Drozd právě nemůže odpovědět, zkuste to prosím za chvíli.";
        var logs = new CapturingLoggerProvider();
        using var factory = CreateFailingBotFactory(
            upstreamDetail,
            logs,
            failReset: false);
        using var client = await CreateAuthenticatedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/consult/loremaster",
            new ConsultRequestDto("Co ví Drozd?"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(userDetail, body);
        Assert.DoesNotContain(upstreamDetail, body);

        var entry = Assert.Single(logs.Entries, e =>
            e.Level == LogLevel.Warning
            && e.State.ContainsKey("UpstreamDetail"));
        Assert.Equal("loremaster", entry.State["Persona"]);
        Assert.Equal(HttpStatusCode.InternalServerError, entry.State["StatusCode"]);
        Assert.Equal(upstreamDetail, entry.State["UpstreamDetail"]);
    }

    [Fact]
    public async Task Reset_UpstreamFailure_UsesFixedCzechDetail_AndLogsRawDetail()
    {
        const string upstreamDetail = "<html>SECRET upstream body</html>";
        const string userDetail = "Reset historie selhal, zkuste to prosím za chvíli.";
        var logs = new CapturingLoggerProvider();
        using var factory = CreateFailingBotFactory(
            upstreamDetail,
            logs,
            failReset: true);
        using var client = await CreateAuthenticatedClientAsync(factory);

        var response = await client.PostAsync("/api/consult/rulemaster/reset", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(userDetail, body);
        Assert.DoesNotContain(upstreamDetail, body);

        var entry = Assert.Single(logs.Entries, e =>
            e.Level == LogLevel.Warning
            && e.State.ContainsKey("UpstreamDetail"));
        Assert.Equal("rulemaster", entry.State["Persona"]);
        Assert.Equal(HttpStatusCode.InternalServerError, entry.State["StatusCode"]);
        Assert.Equal(upstreamDetail, entry.State["UpstreamDetail"]);
    }

    private WebApplicationFactory<Program> CreateFailingBotFactory(
        string upstreamDetail,
        CapturingLoggerProvider logs,
        bool failReset)
        => Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBotConsultClient>();
                services.AddSingleton<IBotConsultClient>(
                    new FailingBotConsultClient(upstreamDetail, failReset));
                services.RemoveAll<ILoggerFactory>();
                services.AddSingleton<ILoggerFactory>(_ =>
                    LoggerFactory.Create(logging => logging.AddProvider(logs)));
            });
        });

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(
        WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var tokenResponse = await client.PostAsJsonAsync(
            "/api/auth/dev-token",
            new DevTokenRequest("consult-test", "consult@ovcina.cz", "Consult Tester"));
        tokenResponse.EnsureSuccessStatusCode();

        var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>()
            ?? throw new InvalidOperationException("Dev-token endpoint returned an empty body.");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.Token);

        return client;
    }

    private sealed class FailingBotConsultClient(string upstreamDetail, bool failReset)
        : IBotConsultClient
    {
        public bool IsEnabled => true;

        public Task<ConsultAnswerDto> AskAsync(
            string persona,
            string message,
            string userEmail,
            string userRole,
            CancellationToken ct = default)
            => failReset
                ? Task.FromResult(new ConsultAnswerDto("ok", 0))
                : throw new BotConsultUpstreamException(
                    HttpStatusCode.InternalServerError,
                    upstreamDetail);

        public Task ResetAsync(
            string persona,
            string userEmail,
            CancellationToken ct = default)
            => failReset
                ? throw new BotConsultUpstreamException(
                    HttpStatusCode.InternalServerError,
                    upstreamDetail)
                : Task.CompletedTask;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName)
            => new CapturingLogger(categoryName, Entries);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(
        string categoryName,
        ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var structuredState = state as IEnumerable<KeyValuePair<string, object?>>;
            entries.Enqueue(new LogEntry(
                logLevel,
                categoryName,
                formatter(state, exception),
                structuredState?.ToDictionary(pair => pair.Key, pair => pair.Value)
                    ?? new Dictionary<string, object?>()));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Category,
        string Message,
        IReadOnlyDictionary<string, object?> State);
}
