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

    public CreateGameDto ToCreateDto() => new(Name, Edition, StartDate, EndDate, Status);
    public UpdateGameDto ToUpdateDto() => new(Name, Edition, StartDate, EndDate, Status);

    public static GameFormModel FromDetail(GameDetailDto dto) => new()
    {
        Name = dto.Name,
        Edition = dto.Edition,
        StartDate = dto.StartDate,
        EndDate = dto.EndDate,
        Status = dto.Status
    };
}
