namespace OvcinaHra.Shared.Dtos;

public record BuildingListDto(int Id, string Name, int? LocationId, string? LocationName, int GameId, bool IsPrebuilt);

public record BuildingDetailDto(
    int Id, string Name, string? Description, string? ImagePath,
    int? LocationId, int GameId, bool IsPrebuilt);

public record CreateBuildingDto(
    string Name, int GameId,
    string? Description = null, int? LocationId = null, bool IsPrebuilt = false);

public record UpdateBuildingDto(
    string Name, string? Description, int? LocationId, bool IsPrebuilt);
