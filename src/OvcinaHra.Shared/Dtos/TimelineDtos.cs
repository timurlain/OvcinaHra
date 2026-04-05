namespace OvcinaHra.Shared.Dtos;

public record GameTimeSlotDto(int Id, int? InGameYear, DateTime StartTime, TimeSpan Duration, string? Rules, int? BattlefieldBonusId, int GameId);

public record CreateGameTimeSlotDto(int GameId, DateTime StartTime, TimeSpan Duration, int? InGameYear = null, string? Rules = null, int? BattlefieldBonusId = null);

public record UpdateGameTimeSlotDto(DateTime StartTime, TimeSpan Duration, int? InGameYear, string? Rules, int? BattlefieldBonusId);

public record BattlefieldBonusDto(int Id, string? Name, int AttackBonus, int DefenseBonus, string? Description, string? ImagePath, int GameId);

public record CreateBattlefieldBonusDto(int GameId, int AttackBonus = 0, int DefenseBonus = 0, string? Name = null, string? Description = null);

public record UpdateBattlefieldBonusDto(string? Name, int AttackBonus, int DefenseBonus, string? Description);
