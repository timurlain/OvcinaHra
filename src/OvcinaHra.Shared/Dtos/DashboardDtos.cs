namespace OvcinaHra.Shared.Dtos;

// Issue #158 — DTOs powering the Home cockpit (`/api/dashboard/...`).
// Each endpoint returns one of these; the cockpit assembles the page
// from the parallel results client-side.

/// <summary>
/// Powers the StavSvetaStrip — 7 game-scoped catalog counts. Single
/// roundtrip to /api/dashboard/stats?gameId={id}, internally executed
/// as a Task.WhenAll over CountAsync queries to avoid 7 sequential trips.
/// </summary>
public record DashboardStatsDto(
    int LocationsCount,
    int CharactersCount,
    int ItemsCount,
    int SpellsCount,
    int SkillsCount,
    int TreasuresCount,
    int QuestsCount);

/// <summary>
/// One row in the PozorPanel. Severity drives the dot colour; <see cref="TargetRoute"/>
/// is the Czech-localised destination for the click-through (organizer
/// jumps straight to whichever catalog has the gap).
/// </summary>
public record DashboardIssueDto(
    string Key,
    string LabelCs,
    int Count,
    string Severity,   /* "low" | "mid" | "high" */
    string TargetRoute);

public record DashboardIssuesDto(IReadOnlyList<DashboardIssueDto> Issues);

/// <summary>
/// One row in the RecentActivityFeed. Pre-MVP audit log doesn't exist —
/// the endpoint stubs <see cref="OccurredUtc"/> from per-entity
/// <c>UpdatedAtUtc</c> (or <c>Id DESC</c> as a fallback for entities
/// without timestamps) and reports <see cref="Verb"/> as the neutral
/// "upravil" until a real WorldChange table replaces this.
/// </summary>
public record DashboardActivityDto(
    string EntityType,
    int EntityId,
    string EntityName,
    string? ThumbnailUrl,
    string Verb,        /* vytvořil | upravil | smazal — currently always "upravil" */
    string ActorName,
    DateTime OccurredUtc);

/// <summary>
/// One row in the TimelinePreview. <see cref="Status"/> is computed
/// server-side from <see cref="StartTime"/> + <see cref="Duration"/>
/// against DateTime.Now.
/// </summary>
public record TimelineRowDto(
    int SlotId,
    DateTime StartTime,
    TimeSpan Duration,
    string? Title,
    string? LocationName,
    int? LocationId,
    string Status);   /* "Probíhá" | "Brzy" | "Zítra" | "Později" */
