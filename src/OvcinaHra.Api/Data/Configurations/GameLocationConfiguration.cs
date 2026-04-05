using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class GameLocationConfiguration : IEntityTypeConfiguration<GameLocation>
{
    public void Configure(EntityTypeBuilder<GameLocation> builder)
    {
        builder.HasKey(e => new { e.GameId, e.LocationId });
        builder.HasOne(e => e.Game).WithMany(g => g.GameLocations).HasForeignKey(e => e.GameId);
        builder.HasOne(e => e.Location).WithMany(l => l.GameLocations).HasForeignKey(e => e.LocationId);
        builder.HasIndex(e => e.GameId);
    }
}
