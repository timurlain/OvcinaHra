using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

public class WorldChange
{
    public int Id { get; set; }
    public int? GameId { get; set; }
    public required string EntityType { get; set; }
    public int EntityId { get; set; }
    public required string EntityName { get; set; }
    public WorldChangeOperation Operation { get; set; }
    public required string ActorUserId { get; set; }
    public required string ActorDisplayName { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}
