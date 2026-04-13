namespace OvcinaHra.Shared.Domain.Entities;

public class GameEventQuest
{
    public int GameEventId { get; set; }
    public int QuestId { get; set; }

    public GameEvent GameEvent { get; set; } = null!;
    public Quest Quest { get; set; } = null!;
}
