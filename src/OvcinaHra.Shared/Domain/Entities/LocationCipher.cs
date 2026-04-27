using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

public class LocationCipher
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int LocationId { get; set; }
    public CipherSkillKey SkillKey { get; set; }
    public required string MessageRaw { get; set; }
    public required string MessageNormalized { get; set; }
    public int? QuestId { get; set; }

    public Game Game { get; set; } = null!;
    public Location Location { get; set; } = null!;
    public Quest? Quest { get; set; }
}
