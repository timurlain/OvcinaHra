using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

public record CharacterListDto(
    int Id, string Name, string? Race, PlayerClass? Class,
    string? Kingdom, bool IsPlayedCharacter, int? ExternalPersonId);

public record CharacterDetailDto(
    int Id, string Name, string? Race, PlayerClass? Class,
    string? Kingdom, int? BirthYear, string? Notes,
    bool IsPlayedCharacter, int? ExternalPersonId,
    int? ParentCharacterId, string? ParentCharacterName,
    string? ImagePath, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public record CreateCharacterDto(
    string Name,
    string? Race = null,
    PlayerClass? Class = null,
    string? Kingdom = null,
    int? BirthYear = null,
    string? Notes = null,
    bool IsPlayedCharacter = false,
    int? ExternalPersonId = null,
    int? ParentCharacterId = null);

public record UpdateCharacterDto(
    string Name,
    string? Race,
    PlayerClass? Class,
    string? Kingdom,
    int? BirthYear,
    string? Notes,
    bool IsPlayedCharacter,
    int? ExternalPersonId,
    int? ParentCharacterId);

public record CharacterAssignmentDto(
    int Id, int CharacterId, string CharacterName,
    int GameId, int ExternalPersonId,
    bool IsActive, DateTime StartedAtUtc, DateTime? EndedAtUtc);

public record CharacterEventDto(
    int Id, CharacterEventType EventType, string Data,
    string? Location, string OrganizerName, DateTime Timestamp);

public record CreateCharacterEventDto(
    CharacterEventType EventType, string Data, string? Location = null);

public record ScanCharacterDto(
    int CharacterId, string Name, string? Race, PlayerClass? Class,
    string? Kingdom, int CurrentLevel, int TotalXp,
    List<string> Skills, List<CharacterEventDto> RecentEvents);

public record ImportResultDto(int Created, int Updated, int Skipped);
