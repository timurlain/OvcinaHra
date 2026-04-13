using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

public class Npc
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public NpcRole Role { get; set; }
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public string? ImagePath { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<GameNpc> GameNpcs { get; set; } = [];
}
