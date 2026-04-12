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

    public ICollection<GameLocation> GameLocations { get; set; } = [];
    public ICollection<GameItem> GameItems { get; set; } = [];
    public ICollection<GameSecretStash> GameSecretStashes { get; set; } = [];
    public ICollection<GameBuilding> GameBuildings { get; set; } = [];
    public ICollection<CraftingRecipe> CraftingRecipes { get; set; } = [];
    public ICollection<GameMonster> GameMonsters { get; set; } = [];
    public ICollection<MonsterLoot> MonsterLoots { get; set; } = [];
    public ICollection<Quest> Quests { get; set; } = [];
    public ICollection<TreasureQuest> TreasureQuests { get; set; } = [];
    public ICollection<GameTimeSlot> TimeSlots { get; set; } = [];
    public ICollection<BattlefieldBonus> BattlefieldBonuses { get; set; } = [];
}
