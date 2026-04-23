namespace OvcinaHra.Shared.Dtos;

// Catalog (master list)
public record SecretStashListDto(
    int Id, string Name, string? Description,
    int TreasureCount, int GameCount);

public record SecretStashDetailDto(int Id, string Name, string? Description, string? ImagePath);

public record CreateSecretStashDto(string Name, string? Description = null);

public record UpdateSecretStashDto(string Name, string? Description);

// Per-game assignment
public record GameSecretStashDto(
    int GameId, int SecretStashId, string StashName,
    int LocationId, string LocationName,
    int TreasureCount);

public record CreateGameSecretStashDto(int GameId, int SecretStashId, int LocationId);

public record UpdateGameSecretStashDto(int LocationId);
