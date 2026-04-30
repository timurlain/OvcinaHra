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

        return group;
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
