namespace OvcinaHra.Shared.Domain.Entities;

public class QuestEncounter
{
    public int QuestId { get; set; }
    public int MonsterId { get; set; }
    public int Quantity { get; set; }

    public Quest Quest { get; set; } = null!;
    public Monster Monster { get; set; } = null!;
}
