using System.Text.Json.Serialization;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Extensions;

namespace OvcinaHra.Shared.Dtos;

// Issue #207 — Map page (cartographer's workshop) DTOs.
//
// Scope cut from the original brief: only Locations and Stashes layers.
// The Encounters layer + animated quest paths require a schema primitive
// that doesn't exist yet — QuestEncounter has no location anchor, no order.
// A follow-up issue will add a QuestWaypoint entity; until then those
// surfaces stay out of scope. Treasures-by-stage explorer (the brief's
// other "wow") still ships in this PR.

/// <summary>
/// Single payload for the whole map. Two arrays — pins are DOM markers
/// (existing ovcinaMap pattern), so the cockpit only needs lat/lon +
/// display fields here.
/// </summary>
public record MapDataDto(
    IReadOnlyList<MapLocationDto> Locations,
    IReadOnlyList<MapStashDto> Stashes);

public record MapLocationDto(
    int Id,
    string Name,
    double Lat,
    double Lon,
    LocationKind Kind,
    int? KingdomId,
    string? KingdomName,
    string? ThumbnailUrl,
    LocationKind? RenderKind = null,
    string? RenderName = null)
{
    [JsonIgnore]
    public string KindDisplay => Kind.GetDisplayName();

    [JsonIgnore]
    public LocationKind EffectiveKind => RenderKind ?? Kind;

    [JsonIgnore]
    public string EffectiveName => string.IsNullOrWhiteSpace(RenderName) ? Name : RenderName;

    [JsonIgnore]
    public string EffectiveKindDisplay => EffectiveKind.GetDisplayName();
}

/// <summary>
/// One stash pin. Always sits at <see cref="Lat"/>/<see cref="Lon"/> of
/// its <c>GameSecretStash.Location</c>. Brief calls for a treasure-count
/// badge keyed by the highest-stage treasure inside; we expose
/// <see cref="TreasureCount"/> + <see cref="HighestStage"/> so the
/// client can pick the badge color from the stage palette.
/// </summary>
public record MapStashDto(
    int Id,
    int LocationId,
    string Name,
    double Lat,
    double Lon,
    int TreasureCount,
    GameTimePhase? HighestStage);

/// <summary>
/// Right-peek payload for a single location. Aggregates everything the
/// peek panel needs in one trip: treasures grouped by stage, encounters
/// list (currently empty — see scope note above), and a lore preview.
/// </summary>
public record LocationPeekDto(
    int Id,
    string Name,
    string? ThumbnailUrl,
    double Lat,
    double Lon,
    int? KingdomId,
    string? KingdomName,
    LocationKind Kind,
    string KindDisplay,
    IReadOnlyList<LocationPeekStashDto> Stashes,
    IReadOnlyList<TreasuresByStageRowDto> TreasuresByStage,
    string? LorePreview,
    IReadOnlyList<LocationPeekChildDto>? Children = null);

public record LocationPeekStashDto(int Id, string Name, int TreasureCount);

/// <summary>
/// A child / variant location nested under a parent (e.g. "Pod borovicí"
/// inside parent "Lesní mýtina"). Children never get pins on the map,
/// but the parent's peek lists them so the user can navigate to them.
/// </summary>
public record LocationPeekChildDto(int Id, string Name, LocationKind Kind)
{
    [JsonIgnore]
    public string KindDisplay => Kind.GetDisplayName();
}

/// <summary>
/// One row in the treasures-by-stage block. The cockpit renders all
/// four stages in fixed order; empty rows show a "Žádné poklady" line
/// so the layout stays stable.
/// </summary>
public record TreasuresByStageRowDto(
    GameTimePhase Stage,
    int Count,
    IReadOnlyList<TreasuresByStageItemDto> Treasures)
{
    [JsonIgnore]
    public string StageDisplay => Stage.GetDisplayName();
}

public record TreasuresByStageItemDto(int Id, string Name);
