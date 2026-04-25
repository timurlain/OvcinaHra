using System.Text.Json.Serialization;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Extensions;

namespace OvcinaHra.Shared.Dtos;

public record ItemListDto(
    int Id,
    string Name,
    ItemType ItemType,
    string? Effect,
    PhysicalForm? PhysicalForm,
    bool IsCraftable,
    int ReqWarrior,
    int ReqArcher,
    int ReqMage,
    int ReqThief,
    bool IsUnique,
    bool IsLimited,
    string? ImagePath,
    string? Note = null,
    string? ImageUrl = null,
    bool HasRecipe = false)
{
    [JsonIgnore]
    public string ItemTypeDisplay => ItemType.GetDisplayName();

    [JsonIgnore]
    public string PhysicalFormDisplay => PhysicalForm.HasValue ? PhysicalForm.Value.GetDisplayName() : "";

    [JsonIgnore]
    public bool HasImage => !string.IsNullOrEmpty(ImagePath);
}

/// <summary>
/// Flat merged shape used by the "Jen tato hra" items grid — combines all catalog
/// item fields with this game's GameItem configuration (price, stock, sale flags).
/// </summary>
public record GameItemListDto(
    int Id,
    string Name,
    ItemType ItemType,
    string? Effect,
    PhysicalForm? PhysicalForm,
    bool IsCraftable,
    int ReqWarrior,
    int ReqArcher,
    int ReqMage,
    int ReqThief,
    bool IsUnique,
    bool IsLimited,
    string? ImagePath,
    int GameId,
    int? Price,
    int? StockCount,
    bool IsSold,
    string? SaleCondition,
    bool IsFindable,
    string? RecipeSummary,
    string? Note = null,
    string? ImageUrl = null,
    bool HasRecipe = false)
{
    [JsonIgnore]
    public string ItemTypeDisplay => ItemType.GetDisplayName();

    [JsonIgnore]
    public string PhysicalFormDisplay => PhysicalForm.HasValue ? PhysicalForm.Value.GetDisplayName() : "";

    [JsonIgnore]
    public bool HasImage => !string.IsNullOrEmpty(ImagePath);
}

public record ItemDetailDto(
    int Id,
    string Name,
    ItemType ItemType,
    string? Effect,
    PhysicalForm? PhysicalForm,
    bool IsCraftable,
    int ReqWarrior,
    int ReqArcher,
    int ReqMage,
    int ReqThief,
    bool IsUnique,
    bool IsLimited,
    string? ImagePath,
    string? Note = null,
    string? ImageUrl = null,
    bool HasRecipe = false);

public record CreateItemDto(
    string Name,
    ItemType ItemType,
    string? Effect = null,
    PhysicalForm? PhysicalForm = null,
    bool IsCraftable = false,
    int ReqWarrior = 0,
    int ReqArcher = 0,
    int ReqMage = 0,
    int ReqThief = 0,
    bool IsUnique = false,
    bool IsLimited = false,
    string? Note = null);

public record UpdateItemDto(
    string Name,
    ItemType ItemType,
    string? Effect,
    PhysicalForm? PhysicalForm,
    bool IsCraftable,
    int ReqWarrior,
    int ReqArcher,
    int ReqMage,
    int ReqThief,
    bool IsUnique,
    bool IsLimited,
    string? Note = null);

// Per-game item configuration
public record GameItemDto(
    int GameId,
    int ItemId,
    string ItemName,
    int? Price,
    int? StockCount,
    bool IsSold,
    string? SaleCondition,
    bool IsFindable);

public record CreateGameItemDto(
    int GameId,
    int ItemId,
    int? Price = null,
    int? StockCount = null,
    bool IsSold = false,
    string? SaleCondition = null,
    bool IsFindable = false);

public record UpdateGameItemDto(
    int? Price,
    int? StockCount,
    bool IsSold,
    string? SaleCondition,
    bool IsFindable);

public record ItemUsabilityDto(
    int ItemId,
    string ItemName,
    bool CanUse,
    int RequiredLevel,
    string Reason,
    int ReqWarrior,
    int ReqArcher,
    int ReqMage,
    int ReqThief);

// ---- Item detail page aggregate (fed to /items/{id}/usage) ----
// Single round-trip powering the Tvorba / Výskyt / Obchod tabs on ItemDetail.

public record ItemUsageGameRefDto(int GameId, string GameName, int Edition);

public record ItemUsageRecipeDto(
    int RecipeId,
    ItemUsageGameRefDto Game,
    int OutputItemId,
    string OutputItemName,
    int? LocationId,
    string? LocationName,
    List<CraftingIngredientDto> Ingredients,
    List<CraftingBuildingReqDto> BuildingRequirements,
    string? IngredientNotes = null);

public record ItemUsageMonsterLootDto(
    int MonsterId,
    string MonsterName,
    string? MonsterImageUrl,
    ItemUsageGameRefDto Game,
    int Quantity);

public record ItemUsageQuestRewardDto(
    int QuestId,
    string QuestName,
    ItemUsageGameRefDto Game,
    int Quantity);

public record ItemUsageTreasureDto(
    int TreasureQuestId,
    string TreasureQuestTitle,
    ItemUsageGameRefDto Game,
    int Count);

public record ItemUsageShopDto(
    ItemUsageGameRefDto Game,
    int? Price,
    int? StockCount,
    bool IsSold,
    string? SaleCondition,
    bool IsFindable);

public record ItemUsageDto(
    int ItemId,
    string ItemName,
    List<ItemUsageRecipeDto> CraftedBy,
    List<ItemUsageRecipeDto> UsedIn,
    List<ItemUsageMonsterLootDto> MonsterLoot,
    List<ItemUsageQuestRewardDto> QuestRewards,
    List<ItemUsageTreasureDto> Treasures,
    List<ItemUsageShopDto> Shops);
