namespace OvcinaHra.Shared.Domain.Entities;

public class TreasureQuestVerification
{
    public int Id { get; set; }
    public int TreasureQuestId { get; set; }
    public int CharacterAssignmentId { get; set; }
    public int CharacterEventId { get; set; }
    public int VerifiedStashId { get; set; }
    public double? MatchConfidence { get; set; }
    public bool Override { get; set; }
    public string? Reason { get; set; }
    public DateTime VerifiedAtUtc { get; set; } = DateTime.UtcNow;
    public required string OrganizerUserId { get; set; }
    public required string OrganizerName { get; set; }

    public TreasureQuest TreasureQuest { get; set; } = null!;
    public CharacterAssignment Assignment { get; set; } = null!;
    public CharacterEvent Event { get; set; } = null!;
    public SecretStash VerifiedStash { get; set; } = null!;
}
