using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorldActivityType
{
    LocationPlaced = 0,
    CharacterLevelUp = 1,
    QuestCompleted = 2,
    CharacterLevelReverted = 3,
    MonsterDefeated = 4,
    HeroFell = 5,
}
