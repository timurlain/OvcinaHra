using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Domain.ValueObjects;

namespace OvcinaHra.Shared.Domain.Entities;

public class Location
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public LocationKind LocationKind { get; set; }
    public GpsCoordinates? Coordinates { get; set; }
    public string? ImagePath { get; set; }
    public string? PlacementPhotoPath { get; set; }
    public string? NpcInfo { get; set; }
    public string? SetupNotes { get; set; }
    public string? Details { get; set; }
    public string? GamePotential { get; set; }
    public string? Region { get; set; }
    public string? Prompt { get; set; }

    /// <summary>
    /// If set, this location is a variant of the parent (hobbit quest, state change, etc.).
    /// Variants inherit GPS from the parent and are shown grouped in the UI.
    /// Only parent locations (ParentLocationId == null) appear as map pins.
    /// </summary>
    public int? ParentLocationId { get; set; }
    public Location? ParentLocation { get; set; }
    public ICollection<Location> Variants { get; set; } = [];

    public ICollection<GameLocation> GameLocations { get; set; } = [];
    public ICollection<GameSecretStash> GameSecretStashes { get; set; } = [];
    public ICollection<Building> Buildings { get; set; } = [];
    public ICollection<QuestLocationLink> QuestLocations { get; set; } = [];
    public ICollection<TreasureQuest> TreasureQuests { get; set; } = [];
}
