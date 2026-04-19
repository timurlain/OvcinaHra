using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

public record GameListDto(int Id, string Name, int Edition, DateOnly StartDate, DateOnly EndDate, GameStatus Status, int? ExternalGameId);

public record GameDetailDto(
    int Id,
    string Name,
    int Edition,
    DateOnly StartDate,
    DateOnly EndDate,
    GameStatus Status,
    string? ImagePath,
    int? ExternalGameId);

public record CreateGameDto(
    string Name,
    int Edition,
    DateOnly StartDate,
    DateOnly EndDate,
    GameStatus Status = GameStatus.Draft);

public record UpdateGameDto(
    string Name,
    int Edition,
    DateOnly StartDate,
    DateOnly EndDate,
    GameStatus Status);

/// <summary>
/// Links this game to a game in registrace-ovčina.
/// </summary>
public record LinkGameDto(int ExternalGameId);

public record GameSkillDto(
    int GameId,
    int SkillId,
    string SkillName,
    PlayerClass? ClassRestriction,
    int XpCost,
    int? LevelRequirement);

public record UpsertGameSkillRequest(int XpCost, int? LevelRequirement);
