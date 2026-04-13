using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

// Catalog
public record NpcListDto(int Id, string Name, NpcRole Role, string? Description);

public record NpcDetailDto(
    int Id, string Name, NpcRole Role,
    string? Description, string? Notes, string? ImagePath);

public record CreateNpcDto(string Name, NpcRole Role, string? Description = null, string? Notes = null);

public record UpdateNpcDto(string Name, NpcRole Role, string? Description, string? Notes);

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
