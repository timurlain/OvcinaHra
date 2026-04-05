using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

public record GameListDto(int Id, string Name, int Edition, DateOnly StartDate, DateOnly EndDate, GameStatus Status);

public record GameDetailDto(
    int Id,
    string Name,
    int Edition,
    DateOnly StartDate,
    DateOnly EndDate,
    GameStatus Status,
    string? ImagePath);

public record CreateGameDto(
    string Name,
    int Edition,
    DateOnly StartDate,
    DateOnly EndDate,
    GameStatus Status = GameStatus.Draft);

public record UpdateGameDto(
    string Name,
    int Edition,
    DateOnly StartDate,
    DateOnly EndDate,
    GameStatus Status);
