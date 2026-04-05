using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

public record TreasureQuestListDto(int Id, string Title, TreasureQuestDifficulty Difficulty, int? LocationId, int? SecretStashId, int GameId);

public record TreasureQuestDetailDto(
    int Id, string Title, string? Clue, TreasureQuestDifficulty Difficulty,
    int? LocationId, int? SecretStashId, int GameId,
    List<TreasureItemDto> Items);

public record CreateTreasureQuestDto(
    string Title, TreasureQuestDifficulty Difficulty, int GameId,
    string? Clue = null, int? LocationId = null, int? SecretStashId = null);

public record UpdateTreasureQuestDto(
    string Title, string? Clue, TreasureQuestDifficulty Difficulty,
    int? LocationId, int? SecretStashId);

public record TreasureItemDto(int TreasureQuestId, int ItemId, string ItemName, int Count);
public record AddTreasureItemDto(int ItemId, int Count = 1);
