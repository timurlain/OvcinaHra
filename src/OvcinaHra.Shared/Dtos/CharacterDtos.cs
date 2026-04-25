using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

// Character catalog (persistent identity).
// KingdomId / KingdomName / KingdomHexColor are optionally populated from the
// character's active CharacterAssignment for a given gameId at read time — they
// are NOT stored on the Character itself (a character can belong to different
// kingdoms in different games).
public record CharacterListDto(
    int Id, string Name, string? PlayerFullName, Race? Race,
    bool IsPlayedCharacter, int? ExternalPersonId,
    int? KingdomId = null, string? KingdomName = null, string? KingdomHexColor = null,
    string? ImagePath = null, string? ImageUrl = null);

public record CharacterDetailDto(
    int Id, string Name, string? PlayerFirstName, string? PlayerLastName,
    Race? Race, int? BirthYear, string? Notes,
    bool IsPlayedCharacter, int? ExternalPersonId,
    int? ParentCharacterId, string? ParentCharacterName,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc,
    int? KingdomId = null, string? KingdomName = null, string? KingdomHexColor = null,
    int? ActiveAssignmentId = null);

public record CreateCharacterDto(
    string Name,
    string? PlayerFirstName = null,
    string? PlayerLastName = null,
    Race? Race = null,
    int? BirthYear = null,
    string? Notes = null,
    bool IsPlayedCharacter = false,
    int? ExternalPersonId = null,
    int? ParentCharacterId = null);

public record UpdateCharacterDto(
    string Name,
    string? PlayerFirstName,
    string? PlayerLastName,
    Race? Race,
    int? BirthYear,
    string? Notes,
    bool IsPlayedCharacter,
    int? ExternalPersonId,
    int? ParentCharacterId);

// Per-game assignment (per-game snapshot). Kingdom lives here, FK to Kingdoms.
public record CharacterAssignmentDto(
    int Id, int CharacterId, string CharacterName,
    int GameId, int ExternalPersonId, int? RegistraceCharacterId,
    PlayerClass? Class, int? KingdomId, string? KingdomName, string? KingdomHexColor,
    bool IsActive, DateTime StartedAtUtc, DateTime? EndedAtUtc);

public record CreateCharacterAssignmentDto(
    int GameId, int ExternalPersonId,
    PlayerClass? Class = null,
    int? KingdomId = null,
    int? RegistraceCharacterId = null);

public record UpdateCharacterAssignmentDto(
    PlayerClass? Class,
    int? KingdomId,
    bool IsActive);

// Dialog-driven upsert for the current game's kingdom on a character's assignment.
public record SetAssignmentKingdomDto(int GameId, int? KingdomId);

// Event log
public record CharacterEventDto(
    int Id, CharacterEventType EventType, string Data,
    string? Location, string OrganizerName, DateTime Timestamp);

public record CreateCharacterEventDto(
    CharacterEventType EventType, string Data, string? Location = null);

// Scan page composite response
public record ScanCharacterDto(
    int CharacterId, int AssignmentId, int ExternalPersonId,
    string Name, string? PlayerFullName,
    Race? Race, PlayerClass? Class, string? Kingdom, int? BirthYear,
    int CurrentLevel, int TotalXp,
    List<string> Skills, List<CharacterEventDto> RecentEvents);

// Import summary
public record ImportResultDto(int Created, int Updated, int Skipped, List<string> Errors);

/// <summary>
/// Issue #192 — result of <c>POST /api/characters/reimport/{gameId}</c>.
/// The wipe deletes every <see cref="CharacterAssignment"/> for the game
/// plus every imported (<c>ExternalPersonId IS NOT NULL</c>) Character
/// row that has no remaining assignment after the wipe; the import then
/// recreates Characters and Assignments fresh from registrace. The whole
/// operation runs in a SERIALIZABLE transaction so a registrace fetch
/// failure rolls back the wipe instead of leaving an empty roster.
/// </summary>
public record ReimportCharactersResponse(
    int WipedAssignments,
    int WipedCharacters,
    ImportResultDto Imported);
