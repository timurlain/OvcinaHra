namespace OvcinaHra.Shared.Domain.Entities;

public class MonsterTagLink
{
    public int MonsterId { get; set; }
    public int TagId { get; set; }

    public Monster Monster { get; set; } = null!;
    public Tag Tag { get; set; } = null!;
}
