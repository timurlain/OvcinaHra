namespace OvcinaHra.Shared.Domain.Entities;

public class CharacterPersonalQuest
{
    public int CharacterId { get; set; }   // PK, one-to-one: a character has at most 1
    public int PersonalQuestId { get; set; }
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }

    public Character Character { get; set; } = null!;
    public PersonalQuest PersonalQuest { get; set; } = null!;
}
