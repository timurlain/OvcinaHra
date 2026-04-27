using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Services;

/// <summary>
/// Issue #3 — fetches the list of available registrace games for the
/// "Propojit s registrací" picker. Server-to-server only: the API key
/// (<c>IntegrationApi:ApiKey</c>) is held in OvčinaHra config and sent
/// via the <c>X-Api-Key</c> header — the browser never sees it.
/// Mirrors the auth + base-URL pattern used by
/// <see cref="RegistraceImportService"/>.
/// </summary>
public class RegistraceGameService(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<RegistraceGameService> logger)
{
    private readonly string _baseUrl = configuration["IntegrationApi:BaseUrl"] ?? "https://registrace.ovcina.cz";
    private readonly string? _apiKey = configuration["IntegrationApi:ApiKey"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<RegistraceGameDto>> GetAvailableAsync(CancellationToken ct = default)
    {
        const string endpoint = "/api/v1/games";
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{_baseUrl.TrimEnd('/')}{endpoint}");

        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Add("X-Api-Key", _apiKey);

        using var response = await SendAsync(request, endpoint, ct);
        response.EnsureSuccessStatusCode();

        var games = await response.Content.ReadFromJsonAsync<List<RegistraceGameDto>>(JsonOptions, ct);
        return games ?? [];
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, string endpoint, CancellationToken ct)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["Endpoint"] = endpoint
        });
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await httpClient.SendAsync(request, ct);
            logger.LogInformation(
                "Registrace upstream {Endpoint} completed in {ElapsedMs} ms with {Outcome}. StatusCode: {StatusCode}",
                endpoint,
                sw.ElapsedMilliseconds,
                response.IsSuccessStatusCode ? "success" : "error",
                response.StatusCode);
            return response;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogInformation(
                ex,
                "Registrace upstream {Endpoint} completed in {ElapsedMs} ms with {Outcome}",
                endpoint, sw.ElapsedMilliseconds, "timeout");
            throw;
        }
        catch (TaskCanceledException ex) when (ct.IsCancellationRequested)
        {
            logger.LogInformation(
                ex,
                "Registrace upstream {Endpoint} completed in {ElapsedMs} ms with {Outcome}",
                endpoint, sw.ElapsedMilliseconds, "error");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogInformation(
                ex,
                "Registrace upstream {Endpoint} completed in {ElapsedMs} ms with {Outcome}",
                endpoint, sw.ElapsedMilliseconds, "error");
            throw;
        }
    }
}
