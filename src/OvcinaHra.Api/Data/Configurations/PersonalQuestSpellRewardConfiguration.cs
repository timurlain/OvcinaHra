using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data.Configurations;

public class PersonalQuestSpellRewardConfiguration : IEntityTypeConfiguration<PersonalQuestSpellReward>
{
    public void Configure(EntityTypeBuilder<PersonalQuestSpellReward> b)
    {
        b.HasKey(e => new { e.PersonalQuestId, e.SpellId });
        b.Property(e => e.Quantity).HasDefaultValue(1);
        b.ToTable(t => t.HasCheckConstraint("CK_PQSpellReward_Qty_Positive", "\"Quantity\" >= 1"));
        b.HasOne(e => e.PersonalQuest).WithMany(q => q.SpellRewards)
            .HasForeignKey(e => e.PersonalQuestId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(e => e.Spell).WithMany()
            .HasForeignKey(e => e.SpellId).OnDelete(DeleteBehavior.Restrict);
    }
}
