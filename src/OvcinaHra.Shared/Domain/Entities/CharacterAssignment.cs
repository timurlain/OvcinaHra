namespace OvcinaHra.Shared.Domain.Entities;

public class CharacterAssignment
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public int GameId { get; set; }
    public int ExternalPersonId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAtUtc { get; set; }

    public Character Character { get; set; } = null!;
    public ICollection<CharacterEvent> Events { get; set; } = [];
}
