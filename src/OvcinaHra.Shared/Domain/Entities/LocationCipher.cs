using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.Entities;

public class LocationCipher
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int LocationId { get; set; }
    public AdventuringSkill Skill { get; set; }
    public CipherTier Tier { get; set; }
    public CipherContentType ContentType { get; set; }
    public required string RevealText { get; set; }
    public string? CipherText { get; set; }
    public string? LibraryKeyword { get; set; }
    public string? LibraryReward { get; set; }
    public int? LinkedQuestId { get; set; }
    public int? LinkedStashNumber { get; set; }
    public string? OrganizerNotes { get; set; }
    public bool IsClaimed { get; set; }
    public DateTime? ClaimedAtUtc { get; set; }
    public int? ClaimedByCharacterId { get; set; }

    public Game Game { get; set; } = null!;
    public Location Location { get; set; } = null!;
    public Quest? LinkedQuest { get; set; }
    public Character? ClaimedByCharacter { get; set; }

    public bool IsClaimable => Tier is CipherTier.StandardVoucher or CipherTier.FlagshipPaired;
}
