namespace OvcinaHra.Shared.Domain.Entities;

public class EventIdempotency
{
    public int CharacterAssignmentId { get; set; }
    public required string IdempotencyKey { get; set; }
    public int EventId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public CharacterAssignment Assignment { get; set; } = null!;
    public CharacterEvent Event { get; set; } = null!;
}
