namespace OvcinaHra.Shared.Dtos;

public record ImageUploadResult(string BlobKey, string? Url);
public record ImageUrlsDto(string? ImageUrl, string? PlacementUrl, string? StampUrl = null);
