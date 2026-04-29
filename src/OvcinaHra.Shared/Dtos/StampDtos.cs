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
