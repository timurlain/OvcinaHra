using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

public class Character
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Race { get; set; }
    public PlayerClass? Class { get; set; }
    public string? Kingdom { get; set; }
    public int? BirthYear { get; set; }
    public string? Notes { get; set; }
    public bool IsPlayedCharacter { get; set; }
    public int? ExternalPersonId { get; set; }
    public int? ParentCharacterId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Character? ParentCharacter { get; set; }
    public ICollection<Character> Children { get; set; } = [];
    public ICollection<CharacterAssignment> Assignments { get; set; } = [];
}
