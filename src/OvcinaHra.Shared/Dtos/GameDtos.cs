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
    int? ExternalGameId,
    decimal? BoundingBoxSwLat = null,
    decimal? BoundingBoxSwLng = null,
    decimal? BoundingBoxNeLat = null,
    decimal? BoundingBoxNeLng = null);

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
    GameStatus Status,
    decimal? BoundingBoxSwLat = null,
    decimal? BoundingBoxSwLng = null,
    decimal? BoundingBoxNeLat = null,
    decimal? BoundingBoxNeLng = null);

public record GameStampDto(
    int LocationId,
    string LocationName,
    string StampImageUrl,
    IReadOnlyList<GameStampStashDto> Stashes);

public record GameStampStashDto(int Id, string Name);

/// <summary>
/// Links this game to a game in registrace-ovčina.
/// </summary>
public record LinkGameDto(int ExternalGameId);

/// <summary>
/// Issue #3 — picker payload for the "Propojit s registrací" UI. Shape
/// matches registrace's <c>/api/v1/games</c> response verbatim so future
/// fields are forward-compatible without re-mapping. Registrace already
/// filters this list to <c>IsPublished == true</c>; the flag is included
/// for UI labelling only.
/// </summary>
public record RegistraceGameDto(
    int Id,
    string Name,
    string? Description,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    DateTime? RegistrationClosesAtUtc,
    int? TargetPlayerCountTotal,
    bool IsPublished);

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
