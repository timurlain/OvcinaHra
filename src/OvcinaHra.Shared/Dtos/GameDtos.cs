using System.Text.Json.Serialization;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Extensions;

namespace OvcinaHra.Shared.Dtos;

public record GameListDto(int Id, string Name, int Edition, DateOnly StartDate, DateOnly EndDate, GameStatus Status, int? ExternalGameId)
{
    [JsonIgnore]
    public string StatusDisplay => Status.GetDisplayName();
}

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
    int Id,
    int GameId,
    int? TemplateSkillId,
    string Name,
    SkillCategory Category,
    PlayerClass? ClassRestriction,
    string? Effect,
    string? RequirementNotes,
    string? ImagePath,
    int XpCost,
    int? LevelRequirement,
    IReadOnlyList<int> BuildingRequirementIds);

public record CreateGameSkillRequest(
    int? TemplateSkillId,
    string Name,
    SkillCategory Category,
    PlayerClass? ClassRestriction,
    string? Effect,
    string? RequirementNotes,
    IReadOnlyList<int> BuildingRequirementIds,
    int XpCost,
    int? LevelRequirement);

public record UpdateGameSkillRequest(
    string Name,
    SkillCategory Category,
    PlayerClass? ClassRestriction,
    string? Effect,
    string? RequirementNotes,
    IReadOnlyList<int> BuildingRequirementIds,
    int XpCost,
    int? LevelRequirement);
