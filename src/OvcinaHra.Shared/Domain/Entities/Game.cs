using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

public class Game
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int Edition { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public GameStatus Status { get; set; }
    public string? ImagePath { get; set; }

    /// <summary>
    /// ID of this game in registrace-ovčina. Null = not yet linked.
    /// Set via "Link s registrací" once registration opens.
    /// </summary>
    public int? ExternalGameId { get; set; }

    // World bounding box for the map UI — SW corner + NE corner. All four
    // nullable; games without a bbox fall back to a default view. Precision
    // matches the GpsCoordinates convention (Location.Latitude/Longitude):
    // decimal(10,7) via HasPrecision in GameConfiguration.
    public decimal? BoundingBoxSwLat { get; set; }
    public decimal? BoundingBoxSwLng { get; set; }
    public decimal? BoundingBoxNeLat { get; set; }
    public decimal? BoundingBoxNeLng { get; set; }

    public ICollection<GameMapOverlay> MapOverlays { get; set; } = [];
    public ICollection<GameLocation> GameLocations { get; set; } = [];
    public ICollection<GameItem> GameItems { get; set; } = [];
    public ICollection<GameSecretStash> GameSecretStashes { get; set; } = [];
    public ICollection<GameBuilding> GameBuildings { get; set; } = [];
    public ICollection<CraftingRecipe> CraftingRecipes { get; set; } = [];
    public ICollection<BuildingRecipe> BuildingRecipes { get; set; } = [];
    public ICollection<GameMonster> GameMonsters { get; set; } = [];
    public ICollection<MonsterLoot> MonsterLoots { get; set; } = [];
    public ICollection<Quest> Quests { get; set; } = [];
    public ICollection<TreasureQuest> TreasureQuests { get; set; } = [];
    public ICollection<GameTimeSlot> TimeSlots { get; set; } = [];
    public ICollection<BattlefieldBonus> BattlefieldBonuses { get; set; } = [];
    public ICollection<GameNpc> GameNpcs { get; set; } = [];
    public ICollection<GameEvent> GameEvents { get; set; } = [];
    public ICollection<OrganizerRoleAssignment> OrganizerRoleAssignments { get; set; } = [];
}
