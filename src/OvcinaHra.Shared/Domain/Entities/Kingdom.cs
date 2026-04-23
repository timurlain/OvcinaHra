namespace OvcinaHra.Shared.Domain.Entities;

public class Kingdom
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? HexColor { get; set; }
    public string? BadgeImageUrl { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }

    public ICollection<CharacterAssignment> Assignments { get; set; } = [];
}
