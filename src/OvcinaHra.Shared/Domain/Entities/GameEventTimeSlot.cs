namespace OvcinaHra.Shared.Domain.Entities;

public class GameEventTimeSlot
{
    public int GameEventId { get; set; }
    public int GameTimeSlotId { get; set; }

    public GameEvent GameEvent { get; set; } = null!;
    public GameTimeSlot TimeSlot { get; set; } = null!;
}
