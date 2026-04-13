using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

// Character catalog (persistent identity)
public record CharacterListDto(
    int Id, string Name, string? PlayerFullName, string? Race,
    bool IsPlayedCharacter, int? ExternalPersonId);

public record CharacterDetailDto(
    int Id, string Name, string? PlayerFirstName, string? PlayerLastName,
    string? Race, int? BirthYear, string? Notes,
    bool IsPlayedCharacter, int? ExternalPersonId,
    int? ParentCharacterId, string? ParentCharacterName,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public record CreateCharacterDto(
    string Name,
    string? PlayerFirstName = null,
    string? PlayerLastName = null,
    string? Race = null,
    int? BirthYear = null,
    string? Notes = null,
    bool IsPlayedCharacter = false,
    int? ExternalPersonId = null,
    int? ParentCharacterId = null);

public record UpdateCharacterDto(
    string Name,
    string? PlayerFirstName,
    string? PlayerLastName,
    string? Race,
    int? BirthYear,
    string? Notes,
    bool IsPlayedCharacter,
    int? ExternalPersonId,
    int? ParentCharacterId);

// Per-game assignment (per-game snapshot)
public record CharacterAssignmentDto(
    int Id, int CharacterId, string CharacterName,
    int GameId, int ExternalPersonId, int? RegistraceCharacterId,
    PlayerClass? Class, string? Kingdom,
    bool IsActive, DateTime StartedAtUtc, DateTime? EndedAtUtc);

public record CreateCharacterAssignmentDto(
    int GameId, int ExternalPersonId,
    PlayerClass? Class = null,
    string? Kingdom = null,
    int? RegistraceCharacterId = null);

public record UpdateCharacterAssignmentDto(
    PlayerClass? Class,
    string? Kingdom,
    bool IsActive);

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
    string? Race, PlayerClass? Class, string? Kingdom, int? BirthYear,
    int CurrentLevel, int TotalXp,
    List<string> Skills, List<CharacterEventDto> RecentEvents);

// Import summary
public record ImportResultDto(int Created, int Updated, int Skipped, List<string> Errors);
