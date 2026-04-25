using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

public record SkillDto(
    int Id,
    string Name,
    SkillCategory Category,
    PlayerClass? ClassRestriction,
    string? Effect,
    string? RequirementNotes,
    string? ImagePath,
    IReadOnlyList<int> RequiredBuildingIds,
    int UsageCount = 0);

/// <summary>
/// Per-skill usage rollup — how many games hold a `GameSkill` copy of this
/// catalog template, plus which games. Powers the catalog "Smazat zablokováno"
/// badge and the SkillDetail "V tomto roce" tab without forcing every list-page
/// caller to enumerate per-game grids.
/// </summary>
public record SkillUsageDto(
    int SkillId,
    int CopiesCount,
    IReadOnlyList<SkillUsageGameDto> Games);

public record SkillUsageGameDto(
    int GameId,
    int GameSkillId,
    string GameName,
    int Edition);

public record CreateSkillRequest(
    string Name,
    PlayerClass? ClassRestriction,
    string? Effect,
    string? RequirementNotes,
    IReadOnlyList<int> RequiredBuildingIds,
    SkillCategory Category = SkillCategory.Class);

public record UpdateSkillRequest(
    string Name,
    PlayerClass? ClassRestriction,
    string? Effect,
    string? RequirementNotes,
    IReadOnlyList<int> RequiredBuildingIds,
    SkillCategory Category = SkillCategory.Class);
