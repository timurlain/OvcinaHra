namespace OvcinaHra.Shared.Dtos;

public record SearchResultDto(string EntityType, int Id, string Name, string? Description);

public record SearchResponseDto(string Query, int TotalCount, List<SearchResultDto> Results);
