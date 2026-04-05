namespace OvcinaHra.Shared.Domain.Entities;

public class BattlefieldBonus
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public int AttackBonus { get; set; }
    public int DefenseBonus { get; set; }
    public string? Description { get; set; }
    public string? ImagePath { get; set; }
    public int GameId { get; set; }

    public Game Game { get; set; } = null!;
    public ICollection<GameTimeSlot> TimeSlots { get; set; } = [];
}
