using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;

namespace OvcinaHra.Api.Services;

public interface IStampLlmVerifyService
{
    bool IsConfigured { get; }
    Task<StampLlmVerifyResult> VerifyAsync(StampLlmVerifyJob job, CancellationToken ct = default);
    Task<StampLlmRecognizeResult> RecognizeAsync(StampLlmRecognizeJob job, CancellationToken ct = default);
}

public sealed record StampLlmVerifyJob(
    int LocationId,
    string ReferenceLocationName,
    string ReferenceBlobKey,
    StampImagePayload CapturedImage);

/// <summary>
/// Multi-image recognition job: rank captured photo against N reference stamps in a single
/// Anthropic Vision call (or batched 2–3 calls when N &gt; 20).
/// </summary>
public sealed record StampLlmRecognizeJob(
    StampImagePayload CapturedImage,
    IReadOnlyList<StampReference> References);

public sealed record StampReference(
    int LocationId,
    string LocationName,
    string ReferenceBlobKey);

public sealed record StampLlmRecognizeResult(
    IReadOnlyList<StampLlmRankedCandidate> Candidates,
    int TotalReferencesScanned,
    int LatencyMs,
    string RawResponse);

public sealed record StampLlmRankedCandidate(
    int LocationId,
    string LocationName,
    double Confidence);

public sealed record StampImagePayload(byte[] Bytes, string MediaType)
{
    public string Base64Data => Convert.ToBase64String(Bytes);

    public static StampImagePayload ParseCaptured(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new StampLlmValidationException(
                "Neplatný snímek razítka",
                "Zachycený snímek razítka nesmí být prázdný.");

        var input = value.Trim();
        var mediaType = "";
        var comma = input.IndexOf(',');

        if (input.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            if (comma < 0)
                throw InvalidCapturedImage();

            var metadata = input["data:".Length..comma];
            var semicolon = metadata.IndexOf(';');
            mediaType = semicolon >= 0 ? metadata[..semicolon] : metadata;
            input = input[(comma + 1)..];
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(input);
        }
        catch (FormatException)
        {
            throw InvalidCapturedImage();
        }

        if (bytes.Length == 0)
            throw InvalidCapturedImage();

        mediaType = string.IsNullOrWhiteSpace(mediaType)
            ? DetectMediaType(bytes) ?? ""
            : mediaType.Trim().ToLowerInvariant();

        if (!IsSupportedMediaType(mediaType))
            throw InvalidCapturedImage();

        return new StampImagePayload(bytes, mediaType);
    }

    public static StampImagePayload FromReferenceBytes(byte[] bytes)
    {
        var mediaType = DetectMediaType(bytes);
        if (mediaType is null)
        {
            throw new StampLlmValidationException(
                "Neplatné referenční razítko",
                "Referenční obrázek razítka není podporovaný formát.");
        }

        return new StampImagePayload(bytes, mediaType);
    }

    private static StampLlmValidationException InvalidCapturedImage()
        => new(
            "Neplatný snímek razítka",
            "Zachycený snímek musí být platný obrázek v base64.");

    private static bool IsSupportedMediaType(string mediaType)
        => mediaType is "image/jpeg" or "image/png" or "image/gif" or "image/webp";

    private static string? DetectMediaType(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        if (bytes.Length >= 8
            && bytes[0] == 0x89
            && bytes[1] == 0x50
            && bytes[2] == 0x4E
            && bytes[3] == 0x47
            && bytes[4] == 0x0D
            && bytes[5] == 0x0A
            && bytes[6] == 0x1A
            && bytes[7] == 0x0A)
            return "image/png";
        if (bytes.Length >= 6
            && bytes[0] == 0x47
            && bytes[1] == 0x49
            && bytes[2] == 0x46
            && bytes[3] == 0x38)
            return "image/gif";
        if (bytes.Length >= 12
            && bytes[0] == 0x52
            && bytes[1] == 0x49
            && bytes[2] == 0x46
            && bytes[3] == 0x46
            && bytes[8] == 0x57
            && bytes[9] == 0x45
            && bytes[10] == 0x42
            && bytes[11] == 0x50)
            return "image/webp";

        return null;
    }
}

public sealed record StampLlmVerifyResult(
    bool Match,
    double Confidence,
    string Reason,
    string ReferenceLocationName,
    int LatencyMs,
    string RawResponse,
    bool Cached);

public sealed class StampLlmVerifyService : IStampLlmVerifyService, IDisposable
{
    private const string DefaultModel = "claude-haiku-4-5-20251001";
    private const int DefaultMaxTokens = 200;
    private const int DefaultTimeoutSeconds = 15;
    private const int MaxRawResponseLength = 1000;

    private const string SystemPrompt = """
        You are a stamp verification assistant for Ovčina, a Czech children's LARP game.

        You will receive two images:
        1. REFERENCE — a clean digital design of a rubber stamp belonging to a specific location in the game world.
        2. CAPTURED — a photograph of an ink impression of (allegedly) that same stamp, pressed onto white paper by a child during the game and photographed with a phone.

        Your job is to decide whether the CAPTURED stamp impression is the same stamp as the REFERENCE design. Account for:
        - Ink density and coverage variations (children press unevenly).
        - Paper texture and lighting differences.
        - Slight rotation (up to ~30°) and translation.
        - Partial smudges, missing edges, or doubled impressions.
        - The difference between a vector/digital design and a physical ink impression of it.

        Focus on the SILHOUETTE and DISTINCTIVE FEATURES — the overall shape, recognizable elements (e.g. an owl, an anchor, a tree, a runic mark). Ignore color (impressions are usually black-on-white regardless of reference color).

        Respond ONLY with a single JSON object, no prose, no markdown fence:

        {"match": true|false, "confidence": <0.0-1.0>, "reason": "<one Czech sentence, 12–30 words>"}

        confidence interpretation:
        - 0.90+ : silhouette and key features clearly match
        - 0.70-0.89 : likely match, minor doubts (smudge, partial impression)
        - 0.50-0.69 : ambiguous — important features missing or partially obscured
        - below 0.50 : likely a different stamp

        reason guidelines (Czech, plain prose, no diacritic shortcuts):
        - If match=true, describe the matching feature: "Silueta sovy se shoduje s referenčním razítkem."
        - If match=false, describe the discrepancy: "Obrazec vypadá jako kotva, referenční razítko zobrazuje sovu."
        - Be concrete about visual elements; don't say "match/mismatch detected".
        """;

    private readonly IBlobStorageService _blobStorage;
    private readonly ILogger<StampLlmVerifyService> _logger;
    private readonly string _model;
    private readonly int _maxTokens;
    private readonly HttpClient? _httpClient;
    private readonly AnthropicClient? _client;

    public bool IsConfigured { get; }

    public StampLlmVerifyService(
        IConfiguration config,
        ILogger<StampLlmVerifyService> logger,
        IBlobStorageService blobStorage)
    {
        _blobStorage = blobStorage;
        _logger = logger;

        _model = config["Anthropic:Model"] ?? DefaultModel;
        _maxTokens = config.GetValue("Anthropic:MaxTokens", DefaultMaxTokens);
        var timeoutSeconds = config.GetValue("Anthropic:TimeoutSeconds", DefaultTimeoutSeconds);

        var apiKey = config["Anthropic:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            IsConfigured = false;
            _logger.LogWarning("[stamp-llm-server] config-missing graceful-503");
            return;
        }

        IsConfigured = true;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        _client = new AnthropicClient(new APIAuthentication(apiKey), _httpClient);
    }

    public async Task<StampLlmVerifyResult> VerifyAsync(StampLlmVerifyJob job, CancellationToken ct = default)
    {
        if (!IsConfigured || _client is null)
            throw new StampLlmConfigurationException();

        var referenceBytes = await _blobStorage.DownloadAsync(job.ReferenceBlobKey, ct);
        if (referenceBytes is null || referenceBytes.Length == 0)
        {
            throw new StampLlmValidationException(
                "Referenční razítko nelze načíst",
                "K lokalitě je uložená cesta k razítku, ale obrázek se nepodařilo načíst.");
        }

        var referenceImage = StampImagePayload.FromReferenceBytes(referenceBytes);
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "[stamp-llm-server] verify service enter locationId={LocationId} model={Model} referenceBlobKey={ReferenceBlobKey} capturedBytes={CapturedBytes}",
            job.LocationId,
            _model,
            job.ReferenceBlobKey,
            job.CapturedImage.Bytes.Length);

        var systemMessages = new List<SystemMessage>
        {
            new(SystemPrompt, new CacheControl { Type = CacheControlType.ephemeral })
        };

        var messages = new List<Message>
        {
            new()
            {
                Role = RoleType.User,
                Content =
                [
                    new TextContent
                    {
                        Text = $"""
                            REFERENCE — location "{job.ReferenceLocationName}":
                            """
                    },
                    new ImageContent
                    {
                        Source = new ImageSource
                        {
                            MediaType = referenceImage.MediaType,
                            Data = referenceImage.Base64Data
                        },
                        CacheControl = new CacheControl { Type = CacheControlType.ephemeral }
                    },
                    new TextContent
                    {
                        Text = """

                            CAPTURED — child's stamped paper:
                            """
                    },
                    new ImageContent
                    {
                        Source = new ImageSource
                        {
                            MediaType = job.CapturedImage.MediaType,
                            Data = job.CapturedImage.Base64Data
                        }
                    },
                    new TextContent
                    {
                        Text = """

                            Is the captured impression the same stamp as the reference design?
                            """
                    }
                ]
            }
        };

        var parameters = new MessageParameters
        {
            Messages = messages,
            MaxTokens = _maxTokens,
            Model = _model,
            Stream = false,
            Temperature = 0m,
            System = systemMessages,
            PromptCaching = PromptCacheType.FineGrained
        };

        try
        {
            var response = await _client.Messages.GetClaudeMessageAsync(parameters, ct);
            stopwatch.Stop();
            var raw = Truncate(response.Message.ToString(), MaxRawResponseLength);
            var parsed = ParseResponse(raw);
            var cached = response.Usage?.CacheReadInputTokens > 0;

            _logger.LogInformation(
                "[stamp-llm-server] verify service exit locationId={LocationId} match={Match} confidence={Confidence} latencyMs={LatencyMs} cached={Cached} cacheReadTokens={CacheReadTokens} cacheCreationTokens={CacheCreationTokens}",
                job.LocationId,
                parsed.Match,
                parsed.Confidence,
                stopwatch.ElapsedMilliseconds,
                cached,
                response.Usage?.CacheReadInputTokens ?? 0,
                response.Usage?.CacheCreationInputTokens ?? 0);

            return new StampLlmVerifyResult(
                parsed.Match,
                parsed.Confidence,
                parsed.Reason,
                job.ReferenceLocationName,
                (int)stopwatch.ElapsedMilliseconds,
                raw,
                cached);
        }
        catch (RateLimitsExceeded ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "[stamp-llm-server] verify service rate-limited locationId={LocationId} latencyMs={LatencyMs}",
                job.LocationId,
                stopwatch.ElapsedMilliseconds);
            throw new StampLlmRateLimitedException(
                "Anthropic rate limit",
                (int)stopwatch.ElapsedMilliseconds,
                ex.Message,
                ex);
        }
        catch (Exception ex) when (ex is not StampLlmValidationException and not OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "[stamp-llm-server] verify service provider-error locationId={LocationId} latencyMs={LatencyMs}",
                job.LocationId,
                stopwatch.ElapsedMilliseconds);
            throw new StampLlmProviderException(
                "Anthropic API call failed",
                (int)stopwatch.ElapsedMilliseconds,
                ex.Message,
                ex);
        }
    }

    /// <summary>
    /// Anthropic Vision allows up to 20 images per request. With 1 captured image we can fit 19
    /// references per call; if there are more we batch into 2–3 sequential calls and aggregate by
    /// max confidence per location. The vast majority of games stay under one batch.
    /// </summary>
    private const int MaxReferencesPerBatch = 19;

    private const string RecognizeSystemPrompt = """
        You are a stamp recognition assistant for Ovčina, a Czech children's LARP game.

        You will receive:
        1. CAPTURED — a photograph of an ink impression of a rubber stamp on paper, taken with a phone.
        2. A numbered set of REFERENCE — clean digital designs of rubber stamps belonging to specific locations in the game world.

        Your job is to RANK the references by visual similarity to the captured impression. Account for:
        - Ink density and coverage variations (children press unevenly).
        - Paper texture and lighting differences.
        - Slight rotation (up to ~30°) and translation.
        - Partial smudges, missing edges, or doubled impressions.
        - The difference between a vector/digital design and a physical ink impression of it.

        Focus on the SILHOUETTE and DISTINCTIVE FEATURES — the overall shape, recognizable elements
        (e.g. an owl, an anchor, a tree, a runic mark). Ignore color (impressions are usually
        black-on-white regardless of reference color).

        Respond ONLY with a single JSON object, no prose, no markdown fence:

        {"candidates": [{"index": <reference number>, "confidence": <0.0-1.0>}, ...]}

        Rules:
        - Include EVERY reference number exactly once.
        - "index" matches the 1-based number printed before each reference image.
        - "confidence" interpretation:
          - 0.90+ : silhouette and key features clearly match
          - 0.70-0.89 : likely match, minor doubts
          - 0.50-0.69 : ambiguous
          - below 0.50 : unlikely / different stamp
        - Sort by confidence descending.
        - No prose, no comments, JSON only.
        """;

    public async Task<StampLlmRecognizeResult> RecognizeAsync(StampLlmRecognizeJob job, CancellationToken ct = default)
    {
        if (!IsConfigured || _client is null)
            throw new StampLlmConfigurationException();

        if (job.References.Count == 0)
        {
            // Defensive — endpoint should already 400 before we get here. Kept so that
            // future callers don't accidentally pay the round-trip for an empty batch.
            return new StampLlmRecognizeResult(
                Array.Empty<StampLlmRankedCandidate>(),
                0,
                0,
                "{\"candidates\":[]}");
        }

        var batches = await ResolveBatchesAsync(job.References, MaxReferencesPerBatch, ct);
        var resolvedCount = batches.Sum(b => b.Count);
        var stopwatch = Stopwatch.StartNew();
        var aggregated = new Dictionary<int, StampLlmRankedCandidate>();
        var rawResponses = new List<string>(batches.Count);

        _logger.LogInformation(
            "[stamp-llm-server] recognize service enter references={ReferenceCount} resolved={ResolvedCount} batches={BatchCount} model={Model} capturedBytes={CapturedBytes}",
            job.References.Count,
            resolvedCount,
            batches.Count,
            _model,
            job.CapturedImage.Bytes.Length);

        if (resolvedCount == 0)
        {
            stopwatch.Stop();
            return new StampLlmRecognizeResult(
                Array.Empty<StampLlmRankedCandidate>(),
                0,
                (int)stopwatch.ElapsedMilliseconds,
                "{\"candidates\":[]}");
        }

        foreach (var batch in batches)
        {
            ct.ThrowIfCancellationRequested();
            var batchResult = await RecognizeBatchAsync(job.CapturedImage, batch, ct);
            rawResponses.Add(batchResult.RawResponse);

            // Aggregate by max confidence per location across batches. A reference seen
            // in multiple batches (we never split a single ref) cannot happen, but if a
            // future change ever overlaps batches we'd want max-of-confidences anyway.
            foreach (var candidate in batchResult.Candidates)
            {
                if (!aggregated.TryGetValue(candidate.LocationId, out var existing)
                    || candidate.Confidence > existing.Confidence)
                {
                    aggregated[candidate.LocationId] = candidate;
                }
            }
        }

        stopwatch.Stop();

        var ranked = aggregated.Values
            .OrderByDescending(c => c.Confidence)
            .ToList();

        _logger.LogInformation(
            "[stamp-llm-server] recognize service exit references={ReferenceCount} candidates={CandidateCount} top1Confidence={Top1Confidence} latencyMs={LatencyMs}",
            job.References.Count,
            ranked.Count,
            ranked.Count > 0 ? ranked[0].Confidence : 0,
            stopwatch.ElapsedMilliseconds);

        return new StampLlmRecognizeResult(
            ranked,
            resolvedCount,
            (int)stopwatch.ElapsedMilliseconds,
            Truncate(string.Join("\n---\n", rawResponses), MaxRawResponseLength));
    }

    private async Task<StampLlmRecognizeResult> RecognizeBatchAsync(
        StampImagePayload captured,
        IReadOnlyList<(StampReference Reference, byte[] Bytes)> batch,
        CancellationToken ct)
    {
        var content = new List<ContentBase>
        {
            new TextContent { Text = "CAPTURED — child's stamped paper:" },
            new ImageContent
            {
                Source = new ImageSource
                {
                    MediaType = captured.MediaType,
                    Data = captured.Base64Data
                }
            }
        };

        for (var i = 0; i < batch.Count; i++)
        {
            var (reference, bytes) = batch[i];
            var refImage = StampImagePayload.FromReferenceBytes(bytes);
            content.Add(new TextContent
            {
                Text = $"\nREFERENCE {i + 1} — \"{reference.LocationName}\":"
            });
            content.Add(new ImageContent
            {
                Source = new ImageSource
                {
                    MediaType = refImage.MediaType,
                    Data = refImage.Base64Data
                }
            });
        }

        content.Add(new TextContent
        {
            Text = $"\nRank ALL {batch.Count} references by visual similarity to the captured impression. JSON only."
        });

        var systemMessages = new List<SystemMessage>
        {
            new(RecognizeSystemPrompt, new CacheControl { Type = CacheControlType.ephemeral })
        };

        var messages = new List<Message>
        {
            new() { Role = RoleType.User, Content = content }
        };

        // Larger token cap than verify because we emit one JSON entry per reference (≤19 entries).
        var maxTokens = Math.Max(_maxTokens, 50 + batch.Count * 30);

        var parameters = new MessageParameters
        {
            Messages = messages,
            MaxTokens = maxTokens,
            Model = _model,
            Stream = false,
            Temperature = 0m,
            System = systemMessages,
            PromptCaching = PromptCacheType.FineGrained
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await _client!.Messages.GetClaudeMessageAsync(parameters, ct);
            stopwatch.Stop();
            var raw = Truncate(response.Message.ToString(), MaxRawResponseLength);
            var ranked = ParseRecognizeResponse(raw, batch);
            return new StampLlmRecognizeResult(ranked, batch.Count, (int)stopwatch.ElapsedMilliseconds, raw);
        }
        catch (RateLimitsExceeded ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "[stamp-llm-server] recognize service rate-limited references={ReferenceCount} latencyMs={LatencyMs}",
                batch.Count,
                stopwatch.ElapsedMilliseconds);
            throw new StampLlmRateLimitedException(
                "Anthropic rate limit",
                (int)stopwatch.ElapsedMilliseconds,
                ex.Message,
                ex);
        }
        catch (StampLlmProviderException)
        {
            // Already-typed provider exceptions (e.g. malformed JSON from ParseRecognizeResponse)
            // bubble up unchanged. Wrapping here would discard the original Title/RawResponse
            // and replace ex.RawResponse with ex.Message — debugging info lost.
            throw;
        }
        catch (Exception ex) when (ex is not StampLlmValidationException and not OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "[stamp-llm-server] recognize service provider-error references={ReferenceCount} latencyMs={LatencyMs}",
                batch.Count,
                stopwatch.ElapsedMilliseconds);
            throw new StampLlmProviderException(
                "Anthropic API call failed",
                (int)stopwatch.ElapsedMilliseconds,
                ex.Message,
                ex);
        }
    }

    private async Task<List<List<(StampReference Reference, byte[] Bytes)>>> ResolveBatchesAsync(
        IReadOnlyList<StampReference> references,
        int batchSize,
        CancellationToken ct)
    {
        var resolved = new List<(StampReference Reference, byte[] Bytes)>(references.Count);
        foreach (var reference in references)
        {
            var bytes = await _blobStorage.DownloadAsync(reference.ReferenceBlobKey, ct);
            if (bytes is null || bytes.Length == 0)
            {
                _logger.LogWarning(
                    "[stamp-llm-server] recognize skipping missing reference locationId={LocationId} blobKey={BlobKey}",
                    reference.LocationId,
                    reference.ReferenceBlobKey);
                continue;
            }
            resolved.Add((reference, bytes));
        }

        var batches = new List<List<(StampReference, byte[])>>();
        for (var i = 0; i < resolved.Count; i += batchSize)
        {
            batches.Add(resolved.GetRange(i, Math.Min(batchSize, resolved.Count - i)));
        }
        return batches;
    }

    private static List<StampLlmRankedCandidate> ParseRecognizeResponse(
        string raw,
        IReadOnlyList<(StampReference Reference, byte[] Bytes)> batch)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (!root.TryGetProperty("candidates", out var candidatesEl) || candidatesEl.ValueKind != JsonValueKind.Array)
                throw new JsonException("Expected 'candidates' array.");

            var ranked = new List<StampLlmRankedCandidate>(batch.Count);
            foreach (var entry in candidatesEl.EnumerateArray())
            {
                var index = entry.GetProperty("index").GetInt32();
                var confidence = entry.GetProperty("confidence").GetDouble();
                if (index < 1 || index > batch.Count) continue;
                if (confidence < 0) confidence = 0;
                if (confidence > 1) confidence = 1;
                var reference = batch[index - 1].Reference;
                ranked.Add(new StampLlmRankedCandidate(reference.LocationId, reference.LocationName, confidence));
            }

            if (ranked.Count == 0)
                throw new JsonException("Response contained no usable candidates.");

            return ranked;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new StampLlmProviderException(
                "Anthropic returned malformed recognize JSON",
                0,
                $"Anthropic vrátil neplatnou odpověď: {raw}",
                ex);
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
        _httpClient?.Dispose();
    }

    private static ParsedStampLlmResponse ParseResponse(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var match = root.GetProperty("match").GetBoolean();
            var confidence = root.GetProperty("confidence").GetDouble();
            var reason = root.GetProperty("reason").GetString();

            if (confidence is < 0 or > 1 || string.IsNullOrWhiteSpace(reason))
                throw new JsonException("Response fields are outside the expected range.");

            return new ParsedStampLlmResponse(match, confidence, reason.Trim());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new StampLlmProviderException(
                "Anthropic returned malformed JSON",
                0,
                $"Anthropic vrátil neplatnou odpověď: {raw}",
                ex);
        }
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private sealed record ParsedStampLlmResponse(bool Match, double Confidence, string Reason);
}

public class StampLlmValidationException(string title, string detail) : Exception(detail)
{
    public string Title { get; } = title;
    public string Detail { get; } = detail;
}

public sealed class StampLlmConfigurationException()
    : Exception("Configuration key Anthropic:ApiKey is required.");

public class StampLlmProviderException(
    string message,
    int latencyMs,
    string rawResponse,
    Exception? innerException = null) : Exception(message, innerException)
{
    public int LatencyMs { get; } = latencyMs;
    public string RawResponse { get; } = rawResponse;
}

public sealed class StampLlmRateLimitedException(
    string message,
    int latencyMs,
    string rawResponse,
    Exception? innerException = null) : StampLlmProviderException(message, latencyMs, rawResponse, innerException);
