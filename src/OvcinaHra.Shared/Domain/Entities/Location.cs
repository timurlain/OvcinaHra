using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Domain.ValueObjects;

namespace OvcinaHra.Shared.Domain.Entities;

public class Location
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public LocationKind LocationKind { get; set; }
    public GpsCoordinates Coordinates { get; set; } = null!;
    public string? ImagePath { get; set; }
    public string? PlacementPhotoPath { get; set; }
    public string? NpcInfo { get; set; }
    public string? SetupNotes { get; set; }

    public ICollection<GameLocation> GameLocations { get; set; } = [];
    public ICollection<SecretStash> SecretStashes { get; set; } = [];
    public ICollection<Building> Buildings { get; set; } = [];
    public ICollection<QuestLocationLink> QuestLocations { get; set; } = [];
    public ICollection<TreasureQuest> TreasureQuests { get; set; } = [];
}
