using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class GameMapOverlayConfiguration : IEntityTypeConfiguration<GameMapOverlay>
{
    public void Configure(EntityTypeBuilder<GameMapOverlay> builder)
    {
        builder.HasKey(e => new { e.GameId, e.Audience });

        builder.Property(e => e.Audience)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(e => e.OverlayJson)
            .IsRequired();

        builder.HasOne(e => e.Game)
            .WithMany(e => e.MapOverlays)
            .HasForeignKey(e => e.GameId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
