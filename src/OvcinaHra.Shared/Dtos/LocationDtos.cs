using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

public record LocationListDto(
    int Id,
    string Name,
    LocationKind LocationKind,
    string? Region,
    decimal? Latitude,
    decimal? Longitude,
    int? ParentLocationId);

public record LocationDetailDto(
    int Id,
    string Name,
    string? Description,
    string? Details,
    string? GamePotential,
    string? Prompt,
    string? Region,
    LocationKind LocationKind,
    decimal? Latitude,
    decimal? Longitude,
    string? ImagePath,
    string? PlacementPhotoPath,
    string? NpcInfo,
    string? SetupNotes,
    int? ParentLocationId,
    List<LocationVariantDto> Variants);

public record LocationVariantDto(int Id, string Name, LocationKind LocationKind);

public record CreateLocationDto(
    string Name,
    LocationKind LocationKind,
    decimal? Latitude = null,
    decimal? Longitude = null,
    string? Description = null,
    string? Details = null,
    string? GamePotential = null,
    string? Prompt = null,
    string? Region = null,
    string? NpcInfo = null,
    string? SetupNotes = null,
    int? ParentLocationId = null);

public record UpdateLocationDto(
    string Name,
    LocationKind LocationKind,
    decimal? Latitude = null,
    decimal? Longitude = null,
    string? Description = null,
    string? Details = null,
    string? GamePotential = null,
    string? Prompt = null,
    string? Region = null,
    string? NpcInfo = null,
    string? SetupNotes = null,
    int? ParentLocationId = null);

public record GameLocationDto(int GameId, int LocationId);
