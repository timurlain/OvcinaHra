using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

public record LocationCipherSlotDto(
    AdventuringSkill Skill,
    string SkillSlug,
    string SkillName,
    int MaxMessageLetters,
    LocationCipherDto? Cipher)
{
    public AdventuringSkill SkillKey => Skill;
}

public record LocationCipherDto(
    int Id,
    int GameId,
    int LocationId,
    string LocationName,
    AdventuringSkill Skill,
    string SkillSlug,
    string SkillName,
    int MaxMessageLetters,
    CipherTier Tier,
    CipherContentType ContentType,
    bool IsClaimable,
    bool IsClaimed,
    string RevealText,
    string? CipherText,
    string? LibraryKeyword,
    string? LibraryReward,
    int? LinkedQuestId,
    string? LinkedQuestName,
    int? LinkedStashNumber,
    string? OrganizerNotes,
    DateTime? ClaimedAtUtc,
    int? ClaimedByCharacterId,
    string? ClaimedByCharacterName)
{
    public AdventuringSkill SkillKey => Skill;
    public string MessageRaw => RevealText;
    public string MessageNormalized => CipherText ?? "";
    public string EncodedPreview => CipherText ?? "";
    public int EncodedLength => CipherText?.Length ?? 0;
    public int? QuestId => LinkedQuestId;
    public string? QuestName => LinkedQuestName;
}

public record LocationCipherDetailDto(
    int Id,
    int GameId,
    int LocationId,
    string LocationName,
    AdventuringSkill Skill,
    string SkillSlug,
    string SkillName,
    int MaxMessageLetters,
    CipherTier Tier,
    CipherContentType ContentType,
    bool IsClaimable,
    bool IsClaimed,
    string RevealText,
    string? CipherText,
    string? LibraryKeyword,
    string? LibraryReward,
    int? LinkedQuestId,
    string? LinkedQuestName,
    int? LinkedStashNumber,
    string? OrganizerNotes,
    DateTime? ClaimedAtUtc,
    int? ClaimedByCharacterId,
    string? ClaimedByCharacterName);

public record LocationCipherCreateDto(
    int GameId,
    int LocationId,
    AdventuringSkill Skill,
    CipherTier Tier,
    CipherContentType ContentType,
    string RevealText,
    string? CipherText = null,
    string? LibraryKeyword = null,
    string? LibraryReward = null,
    int? LinkedQuestId = null,
    int? LinkedStashNumber = null,
    string? OrganizerNotes = null);

public record LocationCipherUpdateDto(
    int GameId,
    int LocationId,
    AdventuringSkill Skill,
    CipherTier Tier,
    CipherContentType ContentType,
    string RevealText,
    string? CipherText = null,
    string? LibraryKeyword = null,
    string? LibraryReward = null,
    int? LinkedQuestId = null,
    int? LinkedStashNumber = null,
    string? OrganizerNotes = null);

public record LocationCipherBulkImportDto(
    int GameId,
    List<LocationCipherCreateDto> Ciphers);

public record LibraryVoucherDto(
    int Id,
    string LibraryKeyword,
    string? LibraryReward,
    int LocationId,
    string LocationName,
    bool IsClaimed,
    DateTime? ClaimedAtUtc,
    AdventuringSkill Skill,
    string SkillName,
    int? ClaimedByCharacterId,
    string? ClaimedByCharacterName);

public record CipherClaimRequestDto(
    string LibraryKeyword,
    bool LocationStampVerified,
    int CharacterId);

public record CipherClaimResultDto(
    bool Success,
    string Reason,
    string? Reward,
    int? CipherId);

public record UpsertLocationCipherDto(string MessageRaw, int? QuestId = null);
