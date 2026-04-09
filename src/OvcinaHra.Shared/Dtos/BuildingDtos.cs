namespace OvcinaHra.Shared.Dtos;

public record BuildingListDto(int Id, string Name, int? LocationId, string? LocationName, bool IsPrebuilt);

public record BuildingDetailDto(
    int Id, string Name, string? Description, string? ImagePath,
    int? LocationId, bool IsPrebuilt);

public record CreateBuildingDto(
    string Name,
    string? Description = null, int? LocationId = null, bool IsPrebuilt = false);

public record UpdateBuildingDto(
    string Name, string? Description, int? LocationId, bool IsPrebuilt);

// Game assignment
public record GameBuildingDto(int GameId, int BuildingId);
