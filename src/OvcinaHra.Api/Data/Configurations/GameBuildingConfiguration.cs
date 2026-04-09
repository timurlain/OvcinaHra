using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class GameBuildingConfiguration : IEntityTypeConfiguration<GameBuilding>
{
    public void Configure(EntityTypeBuilder<GameBuilding> builder)
    {
        builder.HasKey(e => new { e.GameId, e.BuildingId });
        builder.HasOne(e => e.Game).WithMany(g => g.GameBuildings).HasForeignKey(e => e.GameId);
        builder.HasOne(e => e.Building).WithMany(b => b.GameBuildings).HasForeignKey(e => e.BuildingId);
        builder.HasIndex(e => e.GameId);
    }
}
