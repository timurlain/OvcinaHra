namespace OvcinaHra.Shared.Domain.Entities;

public class GamePersonalQuest
{
    public int GameId { get; set; }
    public int PersonalQuestId { get; set; }
    public int? XpCost { get; set; }
    public int? PerKingdomLimit { get; set; }

    public Game Game { get; set; } = null!;
    public PersonalQuest PersonalQuest { get; set; } = null!;
}
