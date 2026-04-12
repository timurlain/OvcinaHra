using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

public record ItemListDto(int Id, string Name, ItemType ItemType, bool IsCraftable, bool IsUnique, bool IsLimited);

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
    string? ImagePath);

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
    bool IsLimited = false);

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
    bool IsLimited);

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
