using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

// Catalog (master list)
public record SecretStashListDto(
    int Id, string Name, string? Description,
    int TreasureCount, int GameCount,
    string? ImagePath = null, string? ImageUrl = null);

public record SecretStashDetailDto(int Id, string Name, string? Description, string? ImagePath, string? ImageUrl = null);

public record CreateSecretStashDto(string Name, string? Description = null);

public record UpdateSecretStashDto(string Name, string? Description);

// Per-game assignment
public record GameSecretStashDto(
    int GameId, int SecretStashId, string StashName,
    int LocationId, string LocationName,
    int TreasureCount,
    string? ImagePath = null, string? ImageUrl = null);

public record CreateGameSecretStashDto(int GameId, int SecretStashId, int LocationId);

public record UpdateGameSecretStashDto(int LocationId);

// Detail page: stash + per-game placement + treasures hidden in this game.
public record SecretStashGameDetailDto(
    int StashId, string StashName, string? Description,
    string? ImagePath, string? ImageUrl,
    int GameId, string GameName, int Edition,
    int? LocationId, string? LocationName,
    List<StashTreasureDto> Treasures);

public record StashTreasureDto(
    int TreasureQuestId, string Title, string? Clue, GameTimePhase Difficulty,
    List<StashTreasureItemDto> Items);

public record StashTreasureItemDto(int ItemId, string ItemName, int Count);
