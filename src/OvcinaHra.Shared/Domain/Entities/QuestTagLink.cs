namespace OvcinaHra.Shared.Domain.Entities;

public class QuestTagLink
{
    public int QuestId { get; set; }
    public int TagId { get; set; }

    public Quest Quest { get; set; } = null!;
    public Tag Tag { get; set; } = null!;
}
