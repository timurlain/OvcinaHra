using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class CharacterConfiguration : IEntityTypeConfiguration<Character>
{
    public void Configure(EntityTypeBuilder<Character> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.PlayerFirstName).HasMaxLength(100);
        builder.Property(e => e.PlayerLastName).HasMaxLength(100);
        builder.Property(e => e.Race).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(e => e.ExternalPersonId).IsUnique().HasFilter("\"ExternalPersonId\" IS NOT NULL");
        builder.HasOne(e => e.ParentCharacter).WithMany(e => e.Children).HasForeignKey(e => e.ParentCharacterId);
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}

public class CharacterAssignmentConfiguration : IEntityTypeConfiguration<CharacterAssignment>
{
    public void Configure(EntityTypeBuilder<CharacterAssignment> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Class).HasConversion<string>().HasMaxLength(20);
        builder.HasOne(e => e.Character).WithMany(c => c.Assignments).HasForeignKey(e => e.CharacterId);
        builder.HasOne(e => e.Kingdom).WithMany(k => k.Assignments).HasForeignKey(e => e.KingdomId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(e => e.GameId);
        builder.HasIndex(e => e.ExternalPersonId);
        builder.HasIndex(e => e.CharacterId);
        builder.HasIndex(e => new { e.GameId, e.ExternalPersonId }).IsUnique();
    }
}

public class CharacterEventConfiguration : IEntityTypeConfiguration<CharacterEvent>
{
    public void Configure(EntityTypeBuilder<CharacterEvent> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EventType).HasConversion<string>().HasMaxLength(30);
        builder.HasOne(e => e.Assignment).WithMany(a => a.Events).HasForeignKey(e => e.CharacterAssignmentId);
        builder.HasIndex(e => e.CharacterAssignmentId);
    }
}

public class EventIdempotencyConfiguration : IEntityTypeConfiguration<EventIdempotency>
{
    public void Configure(EntityTypeBuilder<EventIdempotency> builder)
    {
        builder.HasKey(e => new { e.CharacterAssignmentId, e.IdempotencyKey });
        builder.Property(e => e.IdempotencyKey).HasMaxLength(200);

        builder.HasOne(e => e.Assignment)
            .WithMany()
            .HasForeignKey(e => e.CharacterAssignmentId);
        builder.HasOne(e => e.Event)
            .WithMany()
            .HasForeignKey(e => e.EventId);

        builder.HasIndex(e => e.EventId);
        builder.HasIndex(e => e.CreatedAtUtc);
    }
}
