using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

// Issue #158 — DTOs powering the Home cockpit (`/api/dashboard/...`).
// Each endpoint returns one of these; the cockpit assembles the page
// from the parallel results client-side.

/// <summary>
/// Powers the StavSvetaStrip — 7 game-scoped catalog counts returned
/// from a single call to /api/dashboard/stats?gameId={id}. The endpoint
/// aggregates these server-side via sequential CountAsync awaits (one
/// shared DbContext can't serve concurrent queries; one HTTP roundtrip
/// from the cockpit is what matters).
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

public record DashboardRecentEventDto(
    int EventId,
    int CharacterAssignmentId,
    int CharacterId,
    string CharacterName,
    CharacterEventType EventType,
    string Data,
    string? Location,
    string OrganizerName,
    DateTime Timestamp);

/// <summary>
/// One row in the TimelinePreview. <see cref="IsRunning"/> is computed
/// server-side from <see cref="StartTime"/> + <see cref="Duration"/>
/// against DateTime.UtcNow — TZ-safe. The cockpit then localises
/// "Brzy / Zítra / Později" buckets in the client using local time so
/// near-midnight slots don't get the wrong day for non-UTC users.
/// </summary>
public record TimelineRowDto(
    int SlotId,
    DateTime StartTime,
    TimeSpan Duration,
    string? Title,
    string? LocationName,
    int? LocationId,
    bool IsRunning);
