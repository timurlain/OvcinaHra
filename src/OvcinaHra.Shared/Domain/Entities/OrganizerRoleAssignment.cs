namespace OvcinaHra.Shared.Domain.Entities;

public class OrganizerRoleAssignment
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int GameTimeSlotId { get; set; }
    public int NpcId { get; set; }

    public int PersonId { get; set; }
    public required string PersonName { get; set; }
    public string? PersonEmail { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Game Game { get; set; } = null!;
    public GameTimeSlot TimeSlot { get; set; } = null!;
    public Npc Npc { get; set; } = null!;
}
