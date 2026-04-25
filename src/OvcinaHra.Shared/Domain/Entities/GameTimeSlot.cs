using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

public class GameTimeSlot
{
    public int Id { get; set; }
    public int? InGameYear { get; set; }
    public DateTime StartTime { get; set; }
    public TimeSpan Duration { get; set; }
    public TreasureQuestDifficulty Stage { get; set; } = TreasureQuestDifficulty.Start;
    public string? Rules { get; set; }
    public int? BattlefieldBonusId { get; set; }
    public int GameId { get; set; }

    public BattlefieldBonus? BattlefieldBonus { get; set; }
    public Game Game { get; set; } = null!;
    public ICollection<GameEventTimeSlot> EventTimeSlots { get; set; } = [];
}
