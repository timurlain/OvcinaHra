using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class GameItemConfiguration : IEntityTypeConfiguration<GameItem>
{
    public void Configure(EntityTypeBuilder<GameItem> builder)
    {
        builder.HasKey(e => new { e.GameId, e.ItemId });
        builder.HasOne(e => e.Game).WithMany(g => g.GameItems).HasForeignKey(e => e.GameId);
        builder.HasOne(e => e.Item).WithMany(i => i.GameItems).HasForeignKey(e => e.ItemId);
        builder.HasIndex(e => e.GameId);
    }
}
