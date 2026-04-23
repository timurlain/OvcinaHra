namespace OvcinaHra.Shared.Dtos;

public record KingdomDto(
    int Id,
    string Name,
    string? HexColor,
    string? BadgeImageUrl,
    string? Description,
    int SortOrder,
    int AssignmentCount = 0);

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
