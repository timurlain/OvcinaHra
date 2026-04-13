namespace OvcinaHra.Shared.Domain.Entities;

public class GameEvent
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Game Game { get; set; } = null!;
    public ICollection<GameEventTimeSlot> EventTimeSlots { get; set; } = [];
    public ICollection<GameEventLocation> EventLocations { get; set; } = [];
    public ICollection<GameEventQuest> EventQuests { get; set; } = [];
    public ICollection<GameEventNpc> EventNpcs { get; set; } = [];
}
