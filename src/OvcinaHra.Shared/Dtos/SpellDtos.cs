using System.Text.Json.Serialization;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Extensions;

namespace OvcinaHra.Shared.Dtos;

// ── Catalog (template) DTOs ────────────────────────────────────────────────

public record SpellListDto(
    int Id,
    string Name,
    int Level,
    SpellSchool School,
    bool IsScroll,
    bool IsReaction,
    bool IsLearnable,
    int ManaCost,
    int MinMageLevel,
    int? Price,
    string Effect,
    string? Description)
{
    [JsonIgnore] public string SchoolDisplay => School.GetDisplayName();
}

public record SpellDetailDto(
    int Id,
    string Name,
    int Level,
    int ManaCost,
    SpellSchool School,
    bool IsScroll,
    bool IsReaction,
    bool IsLearnable,
    int MinMageLevel,
    int? Price,
    string Effect,
    string? Description,
    string? ImagePath)
{
    [JsonIgnore] public string SchoolDisplay => School.GetDisplayName();
}

public record CreateSpellDto(
    string Name,
    int Level,
    int ManaCost,
    SpellSchool School,
    bool IsScroll,
    bool IsReaction,
    bool IsLearnable,
    int MinMageLevel,
    int? Price,
    string Effect,
    string? Description = null);

public record UpdateSpellDto(
    string Name,
    int Level,
    int ManaCost,
    SpellSchool School,
    bool IsScroll,
    bool IsReaction,
    bool IsLearnable,
    int MinMageLevel,
    int? Price,
    string Effect,
    string? Description);

// ── Per-game spell configuration (mirror GameItem) ────────────────────────

public record GameSpellDto(
    int Id,
    int GameId,
    int SpellId,
    string SpellName,
    int Level,
    SpellSchool School,
    int? Price,
    bool IsFindable,
    string? AvailabilityNotes)
{
    [JsonIgnore] public string SchoolDisplay => School.GetDisplayName();
}

public record CreateGameSpellDto(
    int GameId,
    int SpellId,
    int? Price = null,
    bool IsFindable = false,
    string? AvailabilityNotes = null);

public record UpdateGameSpellDto(
    int? Price,
    bool IsFindable,
    string? AvailabilityNotes);
