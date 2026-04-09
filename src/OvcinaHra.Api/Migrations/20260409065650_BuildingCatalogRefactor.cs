using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class BuildingCatalogRefactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Buildings_Games_GameId",
                table: "Buildings");

            migrationBuilder.DropIndex(
                name: "IX_Buildings_GameId",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "GameId",
                table: "Buildings");

            migrationBuilder.CreateTable(
                name: "GameBuildings",
                columns: table => new
                {
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    BuildingId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameBuildings", x => new { x.GameId, x.BuildingId });
                    table.ForeignKey(
                        name: "FK_GameBuildings_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameBuildings_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameBuildings_BuildingId",
                table: "GameBuildings",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_GameBuildings_GameId",
                table: "GameBuildings",
                column: "GameId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameBuildings");

            migrationBuilder.AddColumn<int>(
                name: "GameId",
                table: "Buildings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_GameId",
                table: "Buildings",
                column: "GameId");

            migrationBuilder.AddForeignKey(
                name: "FK_Buildings_Games_GameId",
                table: "Buildings",
                column: "GameId",
                principalTable: "Games",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
