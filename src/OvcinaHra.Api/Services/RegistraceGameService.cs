using System.Diagnostics;
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
        var url = $"{_baseUrl.TrimEnd('/')}{endpoint}";
        var host = new Uri(url).Host;
        var request = new HttpRequestMessage(HttpMethod.Get,
            url);

        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Add("X-Api-Key", _apiKey);

        logger.LogInformation(
            "[registrace] upstream request prepared endpoint={Endpoint} host={Host} hasApiKey={HasApiKey}",
            endpoint,
            host,
            !string.IsNullOrWhiteSpace(_apiKey));

        using var response = await SendAsync(request, endpoint, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        logger.LogInformation(
            "[registrace] upstream response body read endpoint={Endpoint} statusCode={StatusCode} bodyLen={BodyLength}",
            endpoint,
            (int)response.StatusCode,
            body.Length);

        var games = JsonSerializer.Deserialize<List<RegistraceGameDto>>(body, JsonOptions) ?? [];
        logger.LogInformation(
            "[registrace] upstream parsed {Count} games endpoint={Endpoint}",
            games.Count,
            endpoint);
        return games;
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
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "[registrace] upstream {Endpoint} completed in {ElapsedMs} ms with {Outcome}. StatusCode: {StatusCode}",
                    endpoint, sw.ElapsedMilliseconds, "success", response.StatusCode);
            }
            else
            {
                logger.LogWarning(
                    "[registrace] upstream {Endpoint} completed in {ElapsedMs} ms with {Outcome}. StatusCode: {StatusCode}",
                    endpoint, sw.ElapsedMilliseconds, "error", response.StatusCode);
            }
            return response;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                ex,
                "[registrace] upstream {Endpoint} completed in {ElapsedMs} ms with {Outcome}",
                endpoint, sw.ElapsedMilliseconds, "timeout");
            throw;
        }
        catch (TaskCanceledException ex) when (ct.IsCancellationRequested)
        {
            logger.LogInformation(
                ex,
                "[registrace] upstream {Endpoint} completed in {ElapsedMs} ms with {Outcome}",
                endpoint, sw.ElapsedMilliseconds, "cancelled");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "[registrace] upstream {Endpoint} completed in {ElapsedMs} ms with {Outcome}",
                endpoint, sw.ElapsedMilliseconds, "error");
            throw;
        }
    }
}
