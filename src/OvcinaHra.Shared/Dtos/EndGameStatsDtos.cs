namespace OvcinaHra.Shared.Dtos;

public record EndGameStatsDto(
    KingdomLevelBreakdownDto[] Kingdoms,
    FirstToLevel5Dto? FirstToLevel5,
    KingdomLeaderboardEntryDto[] ExperienceLeaderboard,
    MonsterStatsDto MonsterStats);

public record KingdomLevelBreakdownDto(
    int? KingdomId,
    string KingdomName,
    string ColorHex,
    int HeroCount,
    int TotalLevelsGained,
    decimal AverageLevel,
    LevelCountDto[] Levels);

public record LevelCountDto(int Level, string Label, int HeroCount);

public record FirstToLevel5Dto(
    int CharacterAssignmentId,
    string HeroName,
    int? KingdomId,
    string KingdomName,
    string ColorHex,
    DateTime TimestampUtc);

public record KingdomLeaderboardEntryDto(
    int? KingdomId,
    string KingdomName,
    string ColorHex,
    int HeroCount,
    int TotalLevelsGained,
    decimal AverageLevel);

public record MonsterStatsDto(
    MonsterDefeatEntryDto[] MostDefeatedMonsters,
    HeroFellEntryDto[] HeroesFallenByKingdom,
    int MonsterDefeatedCount,
    int HeroFellCount);

public record MonsterDefeatEntryDto(string MonsterName, int Count);

public record HeroFellEntryDto(
    int? KingdomId,
    string KingdomName,
    string ColorHex,
    int Count);
