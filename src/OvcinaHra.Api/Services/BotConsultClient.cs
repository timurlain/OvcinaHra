using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using OvcinaHra.Api.Configuration;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Services;

public interface IBotConsultClient
{
    bool IsEnabled { get; }
    Task<ConsultAnswerDto> AskAsync(
        string persona,
        string message,
        string userEmail,
        string userRole,
        CancellationToken ct = default);

    Task ResetAsync(
        string persona,
        string userEmail,
        CancellationToken ct = default);
}

public sealed class BotConsultClient : IBotConsultClient
{
    private const string ApiKeyHeaderName = "X-Bot-Api-Key";

    private readonly HttpClient _http;
    private readonly BotConsultOptions _options;

    public BotConsultClient(HttpClient http, IOptions<BotConsultOptions> options)
    {
        _http = http;
        _options = options.Value;

        if (_options.IsConfigured)
        {
            _http.BaseAddress = BuildBaseAddress(_options.Url!);
            _http.DefaultRequestHeaders.Remove(ApiKeyHeaderName);
            _http.DefaultRequestHeaders.Add(ApiKeyHeaderName, _options.ApiKey);
        }
    }

    public bool IsEnabled => _options.IsConfigured;

    public async Task<ConsultAnswerDto> AskAsync(
        string persona,
        string message,
        string userEmail,
        string userRole,
        CancellationToken ct = default)
    {
        EnsureConfigured();

        using var response = await PostAsync(
            $"api/consult/{persona}",
            new BotConsultBotRequest(message, userEmail, userRole),
            ct);
        await ThrowIfUnsuccessfulAsync(response, ct);

        return await response.Content.ReadFromJsonAsync<ConsultAnswerDto>(cancellationToken: ct)
            ?? throw new BotConsultUpstreamException(
                HttpStatusCode.BadGateway,
                "Bot returned an empty consult response.");
    }

    public async Task ResetAsync(
        string persona,
        string userEmail,
        CancellationToken ct = default)
    {
        EnsureConfigured();

        using var response = await PostAsync(
            $"api/consult/{persona}/reset",
            new BotConsultResetRequest(userEmail),
            ct);
        await ThrowIfUnsuccessfulAsync(response, ct);
    }

    private async Task<HttpResponseMessage> PostAsync<TRequest>(
        string path,
        TRequest request,
        CancellationToken ct)
    {
        try
        {
            return await _http.PostAsJsonAsync(path, request, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new BotConsultTimeoutException(ex);
        }
    }

    private static async Task ThrowIfUnsuccessfulAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var detail = await response.Content.ReadAsStringAsync(ct);
        throw new BotConsultUpstreamException(response.StatusCode, detail);
    }

    private void EnsureConfigured()
    {
        if (!IsEnabled)
            throw new BotConsultUnavailableException();
    }

    private static Uri BuildBaseAddress(string url)
    {
        var baseUrl = url.EndsWith("/", StringComparison.Ordinal)
            ? url
            : $"{url}/";
        return new Uri(baseUrl, UriKind.Absolute);
    }

    private sealed record BotConsultBotRequest(string Message, string UserEmail, string UserRole);
    private sealed record BotConsultResetRequest(string UserEmail);
}

public sealed class BotConsultUnavailableException()
    : InvalidOperationException("Bot consult is not configured.");

public sealed class BotConsultTimeoutException(Exception innerException)
    : TimeoutException("Bot consult timed out.", innerException);

public sealed class BotConsultUpstreamException(HttpStatusCode statusCode, string? detail)
    : HttpRequestException($"Bot consult failed with {(int)statusCode} {statusCode}. {detail}")
{
    public HttpStatusCode UpstreamStatusCode { get; } = statusCode;
    public string? Detail { get; } = detail;
}
