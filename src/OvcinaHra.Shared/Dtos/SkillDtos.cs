using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

public record SkillDto(
    int Id,
    string Name,
    PlayerClass? ClassRestriction,
    string? Effect,
    string? RequirementNotes,
    string? ImagePath,
    IReadOnlyList<int> RequiredBuildingIds);

public record CreateSkillRequest(
    string Name,
    PlayerClass? ClassRestriction,
    string? Effect,
    string? RequirementNotes,
    IReadOnlyList<int> RequiredBuildingIds);

public record UpdateSkillRequest(
    string Name,
    PlayerClass? ClassRestriction,
    string? Effect,
    string? RequirementNotes,
    IReadOnlyList<int> RequiredBuildingIds);
