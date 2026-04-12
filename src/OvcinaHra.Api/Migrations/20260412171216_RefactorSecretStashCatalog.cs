using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class RefactorSecretStashCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SecretStashes_Games_GameId",
                table: "SecretStashes");

            migrationBuilder.DropForeignKey(
                name: "FK_SecretStashes_Locations_LocationId",
                table: "SecretStashes");

            migrationBuilder.DropIndex(
                name: "IX_SecretStashes_GameId_Name",
                table: "SecretStashes");

            migrationBuilder.DropIndex(
                name: "IX_SecretStashes_LocationId",
                table: "SecretStashes");

            migrationBuilder.DropColumn(
                name: "GameId",
                table: "SecretStashes");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "SecretStashes");

            migrationBuilder.CreateTable(
                name: "GameSecretStashes",
                columns: table => new
                {
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    SecretStashId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSecretStashes", x => new { x.GameId, x.SecretStashId });
                    table.ForeignKey(
                        name: "FK_GameSecretStashes_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameSecretStashes_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameSecretStashes_SecretStashes_SecretStashId",
                        column: x => x.SecretStashId,
                        principalTable: "SecretStashes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SecretStashes_Name",
                table: "SecretStashes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameSecretStashes_GameId",
                table: "GameSecretStashes",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_GameSecretStashes_LocationId",
                table: "GameSecretStashes",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_GameSecretStashes_SecretStashId",
                table: "GameSecretStashes",
                column: "SecretStashId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameSecretStashes");

            migrationBuilder.DropIndex(
                name: "IX_SecretStashes_Name",
                table: "SecretStashes");

            migrationBuilder.AddColumn<int>(
                name: "GameId",
                table: "SecretStashes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LocationId",
                table: "SecretStashes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_SecretStashes_GameId_Name",
                table: "SecretStashes",
                columns: new[] { "GameId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecretStashes_LocationId",
                table: "SecretStashes",
                column: "LocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_SecretStashes_Games_GameId",
                table: "SecretStashes",
                column: "GameId",
                principalTable: "Games",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SecretStashes_Locations_LocationId",
                table: "SecretStashes",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
