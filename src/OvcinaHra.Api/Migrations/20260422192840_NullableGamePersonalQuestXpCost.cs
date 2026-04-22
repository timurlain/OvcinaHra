using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class NullableGamePersonalQuestXpCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_GamePersonalQuest_XpCost_NonNegative",
                table: "GamePersonalQuests");

            migrationBuilder.AlterColumn<int>(
                name: "XpCost",
                table: "GamePersonalQuests",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GamePersonalQuest_XpCost_NonNegative",
                table: "GamePersonalQuests",
                sql: "\"XpCost\" IS NULL OR \"XpCost\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_GamePersonalQuest_XpCost_NonNegative",
                table: "GamePersonalQuests");

            migrationBuilder.AlterColumn<int>(
                name: "XpCost",
                table: "GamePersonalQuests",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_GamePersonalQuest_XpCost_NonNegative",
                table: "GamePersonalQuests",
                sql: "\"XpCost\" >= 0");
        }
    }
}
