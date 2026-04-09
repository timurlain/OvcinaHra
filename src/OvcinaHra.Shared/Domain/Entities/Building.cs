namespace OvcinaHra.Shared.Domain.Entities;

public class Building
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? ImagePath { get; set; }
    public int? LocationId { get; set; }
    public bool IsPrebuilt { get; set; }

    public Location? Location { get; set; }
    public ICollection<GameBuilding> GameBuildings { get; set; } = [];
    public ICollection<CraftingBuildingRequirement> CraftingRequirements { get; set; } = [];
}
