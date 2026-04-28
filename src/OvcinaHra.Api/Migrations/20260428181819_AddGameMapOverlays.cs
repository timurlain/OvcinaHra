using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGameMapOverlays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameMapOverlays",
                columns: table => new
                {
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    Audience = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OverlayJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameMapOverlays", x => new { x.GameId, x.Audience });
                    table.ForeignKey(
                        name: "FK_GameMapOverlays_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                DO $$ BEGIN RAISE NOTICE '[map-export-pr3] before GameMapOverlays backfill'; END $$;
                """);

            migrationBuilder.Sql("""
                INSERT INTO "GameMapOverlays" ("GameId", "Audience", "OverlayJson")
                SELECT "Id", 'Player', "OverlayJson"
                FROM "Games"
                WHERE "OverlayJson" IS NOT NULL AND btrim("OverlayJson") <> '';
                """);

            migrationBuilder.Sql("""
                DO $$ BEGIN RAISE NOTICE '[map-export-pr3] after GameMapOverlays backfill'; END $$;
                """);

            migrationBuilder.DropColumn(
                name: "OverlayJson",
                table: "Games");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OverlayJson",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Games" AS g
                SET "OverlayJson" = o."OverlayJson"
                FROM "GameMapOverlays" AS o
                WHERE o."GameId" = g."Id" AND o."Audience" = 'Player';
                """);

            migrationBuilder.DropTable(
                name: "GameMapOverlays");
        }
    }
}
