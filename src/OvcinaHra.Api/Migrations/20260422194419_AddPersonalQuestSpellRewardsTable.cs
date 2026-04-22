using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalQuestSpellRewardsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PersonalQuestSpellRewards",
                columns: table => new
                {
                    PersonalQuestId = table.Column<int>(type: "integer", nullable: false),
                    SpellId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalQuestSpellRewards", x => new { x.PersonalQuestId, x.SpellId });
                    table.CheckConstraint("CK_PQSpellReward_Qty_Positive", "\"Quantity\" >= 1");
                    table.ForeignKey(
                        name: "FK_PersonalQuestSpellRewards_PersonalQuests_PersonalQuestId",
                        column: x => x.PersonalQuestId,
                        principalTable: "PersonalQuests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PersonalQuestSpellRewards_Spells_SpellId",
                        column: x => x.SpellId,
                        principalTable: "Spells",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PersonalQuestSpellRewards_SpellId",
                table: "PersonalQuestSpellRewards",
                column: "SpellId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PersonalQuestSpellRewards");
        }
    }
}
