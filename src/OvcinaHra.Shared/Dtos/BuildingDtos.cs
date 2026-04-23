namespace OvcinaHra.Shared.Dtos;

public record BuildingListDto(
    int Id, string Name, string? Description, string? Notes,
    int? LocationId, string? LocationName, bool IsPrebuilt,
    string? ImagePath = null, string? ImageUrl = null);

public record BuildingDetailDto(
    int Id, string Name, string? Description, string? Notes, string? ImagePath,
    int? LocationId, bool IsPrebuilt);

public record CreateBuildingDto(
    string Name,
    string? Description = null, string? Notes = null,
    int? LocationId = null, bool IsPrebuilt = false);

public record UpdateBuildingDto(
    string Name, string? Description, string? Notes,
    int? LocationId, bool IsPrebuilt);

// Game assignment
public record GameBuildingDto(int GameId, int BuildingId);
