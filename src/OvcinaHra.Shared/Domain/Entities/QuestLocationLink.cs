namespace OvcinaHra.Shared.Domain.Entities;

public class QuestLocationLink
{
    public int QuestId { get; set; }
    public int LocationId { get; set; }

    public Quest Quest { get; set; } = null!;
    public Location Location { get; set; } = null!;
}
