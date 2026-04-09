using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class TreasureItemStandalone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TreasureItems_TreasureQuests_TreasureQuestId",
                table: "TreasureItems");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TreasureItems",
                table: "TreasureItems");

            migrationBuilder.AlterColumn<int>(
                name: "TreasureQuestId",
                table: "TreasureItems",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "TreasureItems",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<int>(
                name: "GameId",
                table: "TreasureItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_TreasureItems",
                table: "TreasureItems",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_TreasureItems_GameId_TreasureQuestId",
                table: "TreasureItems",
                columns: new[] { "GameId", "TreasureQuestId" });

            migrationBuilder.CreateIndex(
                name: "IX_TreasureItems_TreasureQuestId",
                table: "TreasureItems",
                column: "TreasureQuestId");

            migrationBuilder.AddForeignKey(
                name: "FK_TreasureItems_Games_GameId",
                table: "TreasureItems",
                column: "GameId",
                principalTable: "Games",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TreasureItems_TreasureQuests_TreasureQuestId",
                table: "TreasureItems",
                column: "TreasureQuestId",
                principalTable: "TreasureQuests",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TreasureItems_Games_GameId",
                table: "TreasureItems");

            migrationBuilder.DropForeignKey(
                name: "FK_TreasureItems_TreasureQuests_TreasureQuestId",
                table: "TreasureItems");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TreasureItems",
                table: "TreasureItems");

            migrationBuilder.DropIndex(
                name: "IX_TreasureItems_GameId_TreasureQuestId",
                table: "TreasureItems");

            migrationBuilder.DropIndex(
                name: "IX_TreasureItems_TreasureQuestId",
                table: "TreasureItems");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "TreasureItems");

            migrationBuilder.DropColumn(
                name: "GameId",
                table: "TreasureItems");

            migrationBuilder.AlterColumn<int>(
                name: "TreasureQuestId",
                table: "TreasureItems",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_TreasureItems",
                table: "TreasureItems",
                columns: new[] { "TreasureQuestId", "ItemId" });

            migrationBuilder.AddForeignKey(
                name: "FK_TreasureItems_TreasureQuests_TreasureQuestId",
                table: "TreasureItems",
                column: "TreasureQuestId",
                principalTable: "TreasureQuests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
