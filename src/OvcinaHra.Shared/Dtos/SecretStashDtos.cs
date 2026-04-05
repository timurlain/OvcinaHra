namespace OvcinaHra.Shared.Dtos;

public record SecretStashListDto(int Id, string Name, int LocationId, string LocationName, int GameId);

public record SecretStashDetailDto(
    int Id,
    string Name,
    string? Description,
    string? ImagePath,
    int LocationId,
    int GameId);

public record CreateSecretStashDto(
    string Name,
    int LocationId,
    int GameId,
    string? Description = null);

public record UpdateSecretStashDto(
    string Name,
    string? Description);
