using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OvcinaHra.Api.Configuration;
using OvcinaHra.Api.Services;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Tests.Services;

public class BotConsultClientTests
{
    [Fact]
    public async Task AskAsync_SendsLockedBotContract()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ConsultAnswerDto("**Ano.**", 17))
        });
        var client = CreateClient(handler);

        var answer = await client.AskAsync(
            "loremaster",
            "Kdo je Drozd?",
            "test@ovcina.cz",
            "organizator");

        Assert.Equal("**Ano.**", answer.Answer);
        Assert.Equal(17, answer.TokensUsed);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://bot.example/api/consult/loremaster", request.Uri);
        Assert.Equal("test-secret", request.ApiKey);

        using var json = JsonDocument.Parse(request.Body);
        var root = json.RootElement;
        Assert.Equal("Kdo je Drozd?", root.GetProperty("message").GetString());
        Assert.Equal("test@ovcina.cz", root.GetProperty("userEmail").GetString());
        Assert.Equal("organizator", root.GetProperty("userRole").GetString());
    }

    [Fact]
    public async Task ResetAsync_SendsUserEmailOnly()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler);

        await client.ResetAsync("rulemaster", "test@ovcina.cz");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://bot.example/api/consult/rulemaster/reset", request.Uri);
        Assert.Equal("test-secret", request.ApiKey);

        using var json = JsonDocument.Parse(request.Body);
        var root = json.RootElement;
        Assert.Equal("test@ovcina.cz", root.GetProperty("userEmail").GetString());
        var property = Assert.Single(root.EnumerateObject());
        Assert.Equal("userEmail", property.Name);
    }

    private static BotConsultClient CreateClient(HttpMessageHandler handler)
        => new(
            new HttpClient(handler),
            Options.Create(new BotConsultOptions
            {
                Url = "https://bot.example",
                ApiKey = "test-secret"
            }));

    private sealed class RecordingHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.RequestUri?.ToString() ?? string.Empty,
                request.Headers.TryGetValues("X-Bot-Api-Key", out var values)
                    ? values.SingleOrDefault()
                    : null,
                body));

            return _responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed record RecordedRequest(string Uri, string? ApiKey, string Body);
}
