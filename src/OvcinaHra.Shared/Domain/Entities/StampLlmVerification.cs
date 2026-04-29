namespace OvcinaHra.Shared.Domain.Entities;

public class StampLlmVerification
{
    public int Id { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public required string OrganizerUserId { get; set; }
    public required string OrganizerName { get; set; }
    public int LocationId { get; set; }
    public int? ContextStashId { get; set; }
    public int? ContextQuestId { get; set; }
    public bool Match { get; set; }
    public double Confidence { get; set; }
    public int LatencyMs { get; set; }
    public required string RawResponse { get; set; }

    public Location Location { get; set; } = null!;
}
