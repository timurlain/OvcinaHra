using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Client.Pages.Locations;

public class LocationFormModel
{
    public string Name { get; set; } = "";
    public LocationKind LocationKind { get; set; } = LocationKind.Village;
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? Description { get; set; }
    public string? Details { get; set; }
    public string? GamePotential { get; set; }
    public string? Region { get; set; }
    public string? NpcInfo { get; set; }
    public string? SetupNotes { get; set; }
    public int? ParentLocationId { get; set; }

    public CreateLocationDto ToCreateDto() => new(Name, LocationKind, Latitude, Longitude, Description,
        Details: Details, GamePotential: GamePotential, Region: Region,
        NpcInfo: NpcInfo, SetupNotes: SetupNotes, ParentLocationId: ParentLocationId);
    public UpdateLocationDto ToUpdateDto() => new(Name, LocationKind, Latitude, Longitude, Description,
        Details: Details, GamePotential: GamePotential, Region: Region,
        NpcInfo: NpcInfo, SetupNotes: SetupNotes, ParentLocationId: ParentLocationId);

    public static LocationFormModel FromDetail(LocationDetailDto dto) => new()
    {
        Name = dto.Name,
        LocationKind = dto.LocationKind,
        Latitude = dto.Latitude,
        Longitude = dto.Longitude,
        Description = dto.Description,
        Details = dto.Details,
        GamePotential = dto.GamePotential,
        Region = dto.Region,
        NpcInfo = dto.NpcInfo,
        SetupNotes = dto.SetupNotes,
        ParentLocationId = dto.ParentLocationId
    };
}
