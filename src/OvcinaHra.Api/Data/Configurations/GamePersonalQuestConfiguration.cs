using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class GamePersonalQuestConfiguration : IEntityTypeConfiguration<GamePersonalQuest>
{
    public void Configure(EntityTypeBuilder<GamePersonalQuest> b)
    {
        b.HasKey(e => new { e.GameId, e.PersonalQuestId });
        b.HasOne(e => e.Game).WithMany()
            .HasForeignKey(e => e.GameId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(e => e.PersonalQuest).WithMany(q => q.GameLinks)
            .HasForeignKey(e => e.PersonalQuestId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(e => e.GameId);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_GamePersonalQuest_XpCost_NonNegative",
                "\"XpCost\" IS NULL OR \"XpCost\" >= 0");
            t.HasCheckConstraint("CK_GamePersonalQuest_PKL_Positive",
                "\"PerKingdomLimit\" IS NULL OR \"PerKingdomLimit\" >= 1");
        });
    }
}
