using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class PersonalQuestItemRewardConfiguration : IEntityTypeConfiguration<PersonalQuestItemReward>
{
    public void Configure(EntityTypeBuilder<PersonalQuestItemReward> b)
    {
        b.HasKey(e => new { e.PersonalQuestId, e.ItemId });
        b.Property(e => e.Quantity).HasDefaultValue(1);
        b.ToTable(t => t.HasCheckConstraint("CK_PQItemReward_Qty_Positive", "\"Quantity\" >= 1"));
        b.HasOne(e => e.PersonalQuest).WithMany(q => q.ItemRewards)
            .HasForeignKey(e => e.PersonalQuestId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(e => e.Item).WithMany()
            .HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
