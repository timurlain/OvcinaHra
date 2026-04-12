using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class GameMonsterConfiguration : IEntityTypeConfiguration<GameMonster>
{
    public void Configure(EntityTypeBuilder<GameMonster> builder)
    {
        builder.HasKey(e => new { e.GameId, e.MonsterId });
        builder.HasOne(e => e.Game).WithMany(g => g.GameMonsters).HasForeignKey(e => e.GameId);
        builder.HasOne(e => e.Monster).WithMany(m => m.GameMonsters).HasForeignKey(e => e.MonsterId);
        builder.HasIndex(e => e.GameId);
    }
}
