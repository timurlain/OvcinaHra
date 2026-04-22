using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class GameSpellConfiguration : IEntityTypeConfiguration<GameSpell>
{
    public void Configure(EntityTypeBuilder<GameSpell> builder)
    {
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.GameId, e.SpellId }).IsUnique();

        builder.Property(e => e.AvailabilityNotes).HasMaxLength(1000);

        builder.HasOne(e => e.Game)
            .WithMany()
            .HasForeignKey(e => e.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Spell)
            .WithMany(s => s.GameSpells)
            .HasForeignKey(e => e.SpellId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
