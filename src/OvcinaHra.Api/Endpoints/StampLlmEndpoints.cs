using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Services;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class StampLlmEndpoints
{
    public static RouteGroupBuilder MapStampLlmEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/stamps").WithTags("Stamps").RequireAuthorization();

        group.MapPost("/verify-llm", VerifyStampAsync);
        group.MapPost("/recognize", RecognizeStashAsync);

        return group;
    }

    private static async Task<Results<Ok<RecognizeStashResponse>, ProblemHttpResult>> RecognizeStashAsync(
        RecognizeStashRequest request,
        WorldDbContext db,
        IStampLlmVerifyService verifier,
        HttpContext http,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("StampLlmEndpoints");
        logger.LogInformation(
            "[stamp-llm-server] recognize endpoint enter gameId={GameId}",
            request.GameId);

        if (!IsOrganizer(http.User))
        {
            return TypedResults.Problem(
                title: "Přístup odepřen",
                detail: "Pouze organizátoři mohou rozpoznávat razítka pomocí LLM.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (!verifier.IsConfigured)
            return AnthropicConfigurationMissing(logger);

        var gameExists = await db.Games.AnyAsync(g => g.Id == request.GameId, ct);
        if (!gameExists)
        {
            return TypedResults.Problem(
                title: "Hra neexistuje",
                detail: $"Hra s ID {request.GameId} neexistuje.",
                statusCode: StatusCodes.Status404NotFound);
        }

        StampImagePayload capturedImage;
        try
        {
            capturedImage = StampImagePayload.ParseCaptured(request.CapturedImageBase64);
        }
        catch (StampLlmValidationException ex)
        {
            return BadRequest(ex.Title, ex.Detail);
        }

        // Reference set: every Location in this game that has a stamp image set.
        // GameLocations is the authoritative game-membership join (mirrors GetGameStamps);
        // we don't filter by GameSecretStashes here because organizers may want to recognize
        // a glejt stamp even from a location that has no stash yet.
        var references = await db.GameLocations
            .AsNoTracking()
            .Where(gl => gl.GameId == request.GameId
                && gl.Location.StampImagePath != null
                && gl.Location.StampImagePath != "")
            .OrderBy(gl => gl.Location.Name)
            .Select(gl => new
            {
                gl.LocationId,
                LocationName = gl.Location.Name,
                StampImagePath = gl.Location.StampImagePath!
            })
            .ToListAsync(ct);

        if (references.Count == 0)
        {
            // Distinct shape with NoReferences=true so the client renders a calm
            // "Tato hra nemá žádné lokace s razítkem" hint instead of an error toast.
            return TypedResults.Ok(new RecognizeStashResponse(
                Array.Empty<StampMatchCandidate>(),
                TotalReferencesScanned: 0,
                LatencyMs: 0,
                NoReferences: true));
        }

        var organizer = GetOrganizer(http.User);
        var job = new StampLlmRecognizeJob(
            capturedImage,
            references.Select(r => new StampReference(r.LocationId, r.LocationName, r.StampImagePath)).ToList());

        StampLlmRecognizeResult result;
        try
        {
            result = await verifier.RecognizeAsync(job, ct);
        }
        catch (StampLlmValidationException ex)
        {
            return BadRequest(ex.Title, ex.Detail);
        }
        catch (StampLlmRateLimitedException ex)
        {
            await AuditRecognizeFailureAsync(db, request, organizer, ex, ct);
            logger.LogWarning(
                ex,
                "[stamp-llm-server] recognize endpoint rate-limited gameId={GameId} rawResponse={RawResponse}",
                request.GameId,
                ex.RawResponse);
            return TypedResults.Problem(
                title: "LLM rozpoznání je dočasně omezené",
                detail: "LLM rozpoznání je dočasně omezené, zkus to za chvíli znovu.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }
        catch (StampLlmConfigurationException)
        {
            return AnthropicConfigurationMissing(logger);
        }
        catch (StampLlmProviderException ex)
        {
            await AuditRecognizeFailureAsync(db, request, organizer, ex, ct);
            logger.LogError(
                ex,
                "[stamp-llm-server] recognize endpoint provider-error gameId={GameId} rawResponse={RawResponse}",
                request.GameId,
                ex.RawResponse);
            return TypedResults.Problem(
                title: "LLM rozpoznání selhalo",
                detail: "LLM rozpoznání selhalo, zkus snímek znovu nebo použij ruční výběr.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        // Top-3 by confidence, with stash projection joined per location. Empty
        // candidates (e.g. all references missing blobs at runtime) collapses to
        // NoReferences=false but Candidates=[] — UI renders "Žádná shoda" hint.
        var topCandidateIds = result.Candidates.Take(3).Select(c => c.LocationId).ToList();

        var stashesByLocation = await db.GameSecretStashes
            .AsNoTracking()
            .Where(gs => gs.GameId == request.GameId && topCandidateIds.Contains(gs.LocationId))
            .OrderBy(gs => gs.SecretStash.Name)
            .Select(gs => new
            {
                gs.LocationId,
                Stash = new StashSummary(
                    gs.SecretStashId,
                    gs.SecretStash.Name,
                    gs.SecretStash.Description)
            })
            .ToListAsync(ct);

        var stashLookup = stashesByLocation
            .GroupBy(x => x.LocationId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<StashSummary>)g.Select(x => x.Stash).ToList());

        var responseCandidates = result.Candidates
            .Take(3)
            .Select(c => new StampMatchCandidate(
                c.LocationId,
                c.LocationName,
                c.Confidence,
                stashLookup.TryGetValue(c.LocationId, out var stashes) ? stashes : Array.Empty<StashSummary>()))
            .ToList();

        // Audit row: top-1 candidate location + match=true if confident, plus aggregated
        // metadata (game id, references scanned). Endpoint cannot be reached without ≥1
        // resolved reference (the NoReferences shortcut returns earlier), so result.Candidates
        // has at least one entry whenever the LLM call succeeds.
        if (result.Candidates.Count > 0)
        {
            db.StampLlmVerifications.Add(ToRecognizeAuditRow(request, organizer, result));
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "[stamp-llm-server] recognize endpoint exit gameId={GameId} candidates={CandidateCount} top1Confidence={Top1Confidence} latencyMs={LatencyMs}",
            request.GameId,
            responseCandidates.Count,
            responseCandidates.Count > 0 ? responseCandidates[0].Confidence : 0,
            result.LatencyMs);

        return TypedResults.Ok(new RecognizeStashResponse(
            responseCandidates,
            result.TotalReferencesScanned,
            result.LatencyMs));
    }

    private static StampLlmVerification ToRecognizeAuditRow(
        RecognizeStashRequest request,
        OrganizerIdentity organizer,
        StampLlmRecognizeResult result)
    {
        var top = result.Candidates[0];
        return new StampLlmVerification
        {
            TimestampUtc = DateTime.UtcNow,
            OrganizerUserId = organizer.UserId,
            OrganizerName = organizer.Name,
            LocationId = top.LocationId,
            ContextStashId = null,
            ContextQuestId = null,
            Match = top.Confidence >= 0.7,
            Confidence = top.Confidence,
            LatencyMs = result.LatencyMs,
            RawResponse = Truncate(result.RawResponse),
            Mode = StampLlmVerification.ModeRecognize,
            GameId = request.GameId,
            ReferencesScanned = result.TotalReferencesScanned
        };
    }

    private static async Task AuditRecognizeFailureAsync(
        WorldDbContext db,
        RecognizeStashRequest request,
        OrganizerIdentity organizer,
        StampLlmProviderException ex,
        CancellationToken ct)
    {
        // For failure audit we don't have a top-1 location to attach to, but the LocationId
        // FK is required. Sentinel of 0 won't satisfy the FK; instead we look up any location
        // in the game to satisfy the constraint, falling back to skipping the audit if the
        // game is empty. Matches the philosophy: audit cost-bearing calls without crashing
        // the response path on a missing FK target.
        var fallbackLocationId = await db.GameLocations
            .Where(gl => gl.GameId == request.GameId)
            .Select(gl => (int?)gl.LocationId)
            .FirstOrDefaultAsync(ct);

        if (fallbackLocationId is null)
            return;

        db.StampLlmVerifications.Add(new StampLlmVerification
        {
            TimestampUtc = DateTime.UtcNow,
            OrganizerUserId = organizer.UserId,
            OrganizerName = organizer.Name,
            LocationId = fallbackLocationId.Value,
            ContextStashId = null,
            ContextQuestId = null,
            Match = false,
            Confidence = 0,
            LatencyMs = ex.LatencyMs,
            RawResponse = Truncate(ex.RawResponse),
            Mode = StampLlmVerification.ModeRecognize,
            GameId = request.GameId,
            ReferencesScanned = 0
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task<Results<Ok<VerifyStampLlmResponse>, ProblemHttpResult>> VerifyStampAsync(
        VerifyStampLlmRequest request,
        WorldDbContext db,
        IStampLlmVerifyService verifier,
        HttpContext http,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("StampLlmEndpoints");
        logger.LogInformation(
            "[stamp-llm-server] endpoint enter locationId={LocationId} contextStashId={ContextStashId} contextQuestId={ContextQuestId}",
            request.LocationId,
            request.ContextStashId,
            request.ContextQuestId);

        if (!IsOrganizer(http.User))
        {
            return TypedResults.Problem(
                title: "Přístup odepřen",
                detail: "Pouze organizátoři mohou ověřovat razítka pomocí LLM.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (!verifier.IsConfigured)
            return AnthropicConfigurationMissing(logger);

        var location = await db.Locations
            .Where(l => l.Id == request.LocationId)
            .Select(l => new { l.Id, l.Name, l.StampImagePath })
            .FirstOrDefaultAsync(ct);

        if (location is null)
        {
            return TypedResults.Problem(
                title: "Lokalita neexistuje",
                detail: $"Lokalita s ID {request.LocationId} neexistuje.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (string.IsNullOrWhiteSpace(location.StampImagePath))
        {
            return BadRequest(
                "Chybí referenční razítko",
                $"Lokalita „{location.Name}“ nemá nastavený referenční obrázek razítka.");
        }

        StampImagePayload capturedImage;
        try
        {
            capturedImage = StampImagePayload.ParseCaptured(request.CapturedImageBase64);
        }
        catch (StampLlmValidationException ex)
        {
            return BadRequest(ex.Title, ex.Detail);
        }

        var organizer = GetOrganizer(http.User);
        var job = new StampLlmVerifyJob(
            location.Id,
            location.Name,
            location.StampImagePath,
            capturedImage);

        try
        {
            var result = await verifier.VerifyAsync(job, ct);
            db.StampLlmVerifications.Add(ToAuditRow(request, organizer, result, location.Id));
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "[stamp-llm-server] endpoint exit locationId={LocationId} match={Match} confidence={Confidence} latencyMs={LatencyMs} cached={Cached}",
                location.Id,
                result.Match,
                result.Confidence,
                result.LatencyMs,
                result.Cached);

            return TypedResults.Ok(new VerifyStampLlmResponse(
                result.Match,
                result.Confidence,
                result.Reason,
                result.ReferenceLocationName,
                result.LatencyMs));
        }
        catch (StampLlmValidationException ex)
        {
            return BadRequest(ex.Title, ex.Detail);
        }
        catch (StampLlmRateLimitedException ex)
        {
            await AuditFailureAsync(db, request, organizer, location.Id, ex, ct);
            logger.LogWarning(
                ex,
                "[stamp-llm-server] endpoint rate-limited locationId={LocationId} rawResponse={RawResponse}",
                location.Id,
                ex.RawResponse);
            return TypedResults.Problem(
                title: "LLM ověření je dočasně omezené",
                detail: "LLM ověření je dočasně omezené, použij ruční výběr.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }
        catch (StampLlmConfigurationException)
        {
            return AnthropicConfigurationMissing(logger);
        }
        catch (StampLlmProviderException ex)
        {
            await AuditFailureAsync(db, request, organizer, location.Id, ex, ct);
            logger.LogError(
                ex,
                "[stamp-llm-server] endpoint provider-error locationId={LocationId} rawResponse={RawResponse}",
                location.Id,
                ex.RawResponse);
            return TypedResults.Problem(
                title: "LLM ověření selhalo",
                detail: "LLM ověření selhalo, použij ruční výběr.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static ProblemHttpResult AnthropicConfigurationMissing(ILogger logger)
    {
        logger.LogWarning("[stamp-llm-server] config-missing graceful-503");
        return TypedResults.Problem(
            title: "LLM ověření není v tomto prostředí nakonfigurované",
            detail: "Nastavte proměnnou prostředí Anthropic__ApiKey pro zapnutí LLM ověřování razítek.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static ProblemHttpResult BadRequest(string title, string detail)
        => TypedResults.Problem(
            title: title,
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);

    private static StampLlmVerification ToAuditRow(
        VerifyStampLlmRequest request,
        OrganizerIdentity organizer,
        StampLlmVerifyResult result,
        int locationId)
        => new()
        {
            TimestampUtc = DateTime.UtcNow,
            OrganizerUserId = organizer.UserId,
            OrganizerName = organizer.Name,
            LocationId = locationId,
            ContextStashId = request.ContextStashId,
            ContextQuestId = request.ContextQuestId,
            Match = result.Match,
            Confidence = result.Confidence,
            LatencyMs = result.LatencyMs,
            RawResponse = Truncate(result.RawResponse)
        };

    private static async Task AuditFailureAsync(
        WorldDbContext db,
        VerifyStampLlmRequest request,
        OrganizerIdentity organizer,
        int locationId,
        StampLlmProviderException ex,
        CancellationToken ct)
    {
        db.StampLlmVerifications.Add(new StampLlmVerification
        {
            TimestampUtc = DateTime.UtcNow,
            OrganizerUserId = organizer.UserId,
            OrganizerName = organizer.Name,
            LocationId = locationId,
            ContextStashId = request.ContextStashId,
            ContextQuestId = request.ContextQuestId,
            Match = false,
            Confidence = 0,
            LatencyMs = ex.LatencyMs,
            RawResponse = Truncate(ex.RawResponse)
        });
        await db.SaveChangesAsync(ct);
    }

    private static string Truncate(string value)
        => value.Length <= 1000 ? value : value[..1000];

    private static OrganizerIdentity GetOrganizer(ClaimsPrincipal user)
    {
        var userId = user.FindFirstValue("sub")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? "unknown";
        var name = user.FindFirstValue("name")
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? userId;
        return new OrganizerIdentity(userId, name);
    }

    private static bool IsOrganizer(ClaimsPrincipal user)
    {
        var roles = user.FindAll(ClaimTypes.Role)
            .Concat(user.FindAll("role"))
            .Select(c => c.Value);

        return roles.Any(role => role is "Organizer" or "Organizator" or "Organizátor" or "Admin");
    }

    private sealed record OrganizerIdentity(string UserId, string Name);
}
