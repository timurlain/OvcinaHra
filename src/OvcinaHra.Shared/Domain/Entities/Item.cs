using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Domain.ValueObjects;

namespace OvcinaHra.Shared.Domain.Entities;

public class Item
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public ItemType ItemType { get; set; }
    public string? Effect { get; set; }
    public PhysicalForm? PhysicalForm { get; set; }
    public bool IsCraftable { get; set; }
    public ClassRequirements ClassRequirements { get; set; } = null!;
    public bool IsUnique { get; set; }
    public bool IsLimited { get; set; }
    public string? ImagePath { get; set; }

    // Free-text organizer note — issue #120. Visible in catalog + game grid
    // columns and on the detail page. Catalog-scoped (no per-game override).
    public string? Note { get; set; }

    public ICollection<GameItem> GameItems { get; set; } = [];
    public ICollection<CraftingIngredient> CraftingIngredients { get; set; } = [];
    public ICollection<MonsterLoot> MonsterLoots { get; set; } = [];
    public ICollection<QuestReward> QuestRewards { get; set; } = [];
    public ICollection<TreasureItem> TreasureItems { get; set; } = [];
}
