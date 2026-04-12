using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

public class CharacterEvent
{
    public int Id { get; set; }
    public int CharacterAssignmentId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public required string OrganizerUserId { get; set; }
    public required string OrganizerName { get; set; }
    public CharacterEventType EventType { get; set; }
    public required string Data { get; set; }
    public string? Location { get; set; }

    public CharacterAssignment Assignment { get; set; } = null!;
}
