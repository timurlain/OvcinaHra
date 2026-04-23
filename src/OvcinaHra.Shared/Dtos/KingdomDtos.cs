namespace OvcinaHra.Shared.Dtos;

// BadgeImageUrl is historically the stored blob key (misnamed — kept for wire
// compatibility). BadgeImageSasUrl is the resolved, short-lived SAS URL
// suitable for <img src=…> binding.
public record KingdomDto(
    int Id,
    string Name,
    string? HexColor,
    string? BadgeImageUrl,
    string? Description,
    int SortOrder,
    int AssignmentCount = 0,
    string? BadgeImageSasUrl = null);

public record CreateKingdomDto(
    string Name,
    string? HexColor = null,
    string? BadgeImageUrl = null,
    string? Description = null,
    int SortOrder = 0);

public record UpdateKingdomDto(
    string Name,
    string? HexColor,
    string? BadgeImageUrl,
    string? Description,
    int SortOrder);
