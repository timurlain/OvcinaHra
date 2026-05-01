namespace OvcinaHra.Shared.Dtos;

public record VerifyStampLlmRequest(
    int LocationId,
    string CapturedImageBase64,
    int? ContextStashId = null,
    int? ContextQuestId = null);

public record VerifyStampLlmResponse(
    bool Match,
    double Confidence,
    string Reason,
    string ReferenceLocationName,
    int LatencyMs);

/// <summary>
/// Request body for POST /api/stamps/recognize. Photo of a glejt stamp →
/// ranked candidate Locations from the hra s daným GameId.
/// </summary>
public record RecognizeStashRequest(
    int GameId,
    string CapturedImageBase64);

/// <summary>
/// Response body for POST /api/stamps/recognize. Top-3 candidates, each with
/// its inline list of stashes so the organizer can guide the player without
/// further navigation. Empty <see cref="Candidates"/> + <c>NoReferences=true</c>
/// means the vybraná hra has no stamped locations yet.
/// </summary>
public record RecognizeStashResponse(
    IReadOnlyList<StampMatchCandidate> Candidates,
    int TotalReferencesScanned,
    int LatencyMs,
    bool NoReferences = false);

public record StampMatchCandidate(
    int LocationId,
    string LocationName,
    double Confidence,
    IReadOnlyList<StashSummary> Stashes);

public record StashSummary(
    int StashId,
    string Name,
    string? Summary);
