using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

public class WorldActivity
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public required string OrganizerUserId { get; set; }
    public required string OrganizerName { get; set; }
    public WorldActivityType ActivityType { get; set; }
    public required string Description { get; set; }
    public int? LocationId { get; set; }
    public int? CharacterAssignmentId { get; set; }
    public int? QuestId { get; set; }
    public string? DataJson { get; set; }

    public Game Game { get; set; } = null!;
    public Location? Location { get; set; }
    public CharacterAssignment? CharacterAssignment { get; set; }
    public Quest? Quest { get; set; }
}
