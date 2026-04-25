using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Client.Pages.Games;

public class GameFormModel
{
    public string Name { get; set; } = "";
    public int Edition { get; set; } = 1;
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(2));
    public GameStatus Status { get; set; } = GameStatus.Draft;

    public decimal? BoundingBoxSwLat { get; set; }
    public decimal? BoundingBoxSwLng { get; set; }
    public decimal? BoundingBoxNeLat { get; set; }
    public decimal? BoundingBoxNeLng { get; set; }

    /// <summary>Issue #3 — read-only mirror of <c>Game.ExternalGameId</c>.
    /// Mutated only by the link/unlink popup, never by the Save button —
    /// keeps the link decoupled from Edit so a user can't accidentally
    /// "save away" a freshly-set link by hitting Cancel.</summary>
    public int? ExternalGameId { get; set; }

    public CreateGameDto ToCreateDto() => new(Name, Edition, StartDate, EndDate, Status);

    public UpdateGameDto ToUpdateDto() => new(
        Name, Edition, StartDate, EndDate, Status,
        BoundingBoxSwLat, BoundingBoxSwLng, BoundingBoxNeLat, BoundingBoxNeLng);

    public static GameFormModel FromDetail(GameDetailDto dto) => new()
    {
        Name = dto.Name,
        Edition = dto.Edition,
        StartDate = dto.StartDate,
        EndDate = dto.EndDate,
        Status = dto.Status,
        BoundingBoxSwLat = dto.BoundingBoxSwLat,
        BoundingBoxSwLng = dto.BoundingBoxSwLng,
        BoundingBoxNeLat = dto.BoundingBoxNeLat,
        BoundingBoxNeLng = dto.BoundingBoxNeLng,
        ExternalGameId = dto.ExternalGameId
    };
}
