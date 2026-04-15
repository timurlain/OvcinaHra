using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

// Catalog
public record NpcListDto(
    int Id, string Name, NpcRole Role, string? Description,
    int? BirthYear, int? DeathYear);

public record NpcDetailDto(
    int Id, string Name, NpcRole Role,
    string? Description, string? Notes, string? ImagePath,
    int? BirthYear, int? DeathYear);

public record CreateNpcDto(
    string Name, NpcRole Role,
    string? Description = null, string? Notes = null,
    int? BirthYear = null, int? DeathYear = null);

public record UpdateNpcDto(
    string Name, NpcRole Role,
    string? Description, string? Notes,
    int? BirthYear = null, int? DeathYear = null);

// Per-game assignment
public record GameNpcDto(
    int GameId, int NpcId, string NpcName, NpcRole Role,
    int? PlayedByPersonId, string? PlayedByName, string? PlayedByEmail,
    string? Notes);

public record CreateGameNpcDto(
    int GameId, int NpcId,
    int? PlayedByPersonId = null, string? PlayedByName = null, string? PlayedByEmail = null,
    string? Notes = null);

public record UpdateGameNpcDto(
    int? PlayedByPersonId, string? PlayedByName, string? PlayedByEmail, string? Notes);

// Adults from registrace — eligible to play NPCs.
public record RegistraceAdultDto(
    int PersonId,
    string FirstName,
    string LastName,
    int? BirthYear,
    string? Email,
    List<string> Roles);
