namespace OvcinaHra.Shared.Domain.Entities;

public class LocalUser
{
    public int Id { get; set; }
    public required string RegistraceUserId { get; set; }
    public string? AvatarColor { get; set; }
    public string? Preferences { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}
