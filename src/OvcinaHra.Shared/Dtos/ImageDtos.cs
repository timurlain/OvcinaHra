namespace OvcinaHra.Shared.Dtos;

public record ImageUploadResult(string BlobKey, string? Url, ImageUploadMetadataDto? Metadata = null);
public record ImageUploadMetadataDto(double? GpsLatitude = null, double? GpsLongitude = null, string? CapturedAt = null);
public record ImageUrlsDto(string? ImageUrl, string? PlacementUrl, string? StampUrl = null);
