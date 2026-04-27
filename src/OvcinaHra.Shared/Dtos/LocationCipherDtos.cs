using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

public record LocationCipherSlotDto(
    CipherSkillKey SkillKey,
    string SkillSlug,
    string SkillName,
    int MaxMessageLetters,
    LocationCipherDto? Cipher);

public record LocationCipherDto(
    int Id,
    int GameId,
    int LocationId,
    CipherSkillKey SkillKey,
    string SkillSlug,
    string SkillName,
    int MaxMessageLetters,
    string MessageRaw,
    string MessageNormalized,
    string EncodedPreview,
    int EncodedLength,
    int? QuestId,
    string? QuestName);

public record UpsertLocationCipherDto(string MessageRaw, int? QuestId = null);
