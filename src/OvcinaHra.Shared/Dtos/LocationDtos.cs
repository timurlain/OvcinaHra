using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

public record LocationListDto(
    int Id,
    string Name,
    LocationKind LocationKind,
    decimal Latitude,
    decimal Longitude);

public record LocationDetailDto(
    int Id,
    string Name,
    string? Description,
    LocationKind LocationKind,
    decimal Latitude,
    decimal Longitude,
    string? ImagePath,
    string? PlacementPhotoPath,
    string? NpcInfo,
    string? SetupNotes);

public record CreateLocationDto(
    string Name,
    LocationKind LocationKind,
    decimal Latitude,
    decimal Longitude,
    string? Description = null,
    string? NpcInfo = null,
    string? SetupNotes = null);

public record UpdateLocationDto(
    string Name,
    LocationKind LocationKind,
    decimal Latitude,
    decimal Longitude,
    string? Description,
    string? NpcInfo,
    string? SetupNotes);

public record GameLocationDto(int GameId, int LocationId);
