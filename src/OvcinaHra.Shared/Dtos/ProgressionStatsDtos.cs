namespace OvcinaHra.Shared.Dtos;

public record ProgressionStatsDto(
    KingdomProgressionDto[] Kingdoms,
    ProgressionEventRow[] Events);

public record KingdomProgressionDto(
    int KingdomId,
    string KingdomName,
    string HexColor,
    int SortOrder,
    TimeSlotBucketDto[] Buckets);

public record TimeSlotBucketDto(
    int TimeSlotId,
    string Label,
    string TimeSlotShortLabel,
    int[] HeroCountByLevel);

public record ProgressionEventRow(
    string PlayerName,
    string CharacterName,
    string KingdomName,
    string KingdomHexColor,
    int TimeSlotId,
    string TimeSlotLabel,
    string TimeSlotShortLabel,
    string EventType,
    int LevelGained,
    DateTime TimestampUtc);
