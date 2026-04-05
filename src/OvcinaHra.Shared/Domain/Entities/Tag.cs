using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

public class Tag
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public TagKind Kind { get; set; }

    public ICollection<MonsterTagLink> MonsterTags { get; set; } = [];
    public ICollection<QuestTagLink> QuestTags { get; set; } = [];
}
