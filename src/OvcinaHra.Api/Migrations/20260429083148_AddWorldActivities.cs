using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorldActivities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorldActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OrganizerUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OrganizerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ActivityType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: true),
                    CharacterAssignmentId = table.Column<int>(type: "integer", nullable: true),
                    QuestId = table.Column<int>(type: "integer", nullable: true),
                    DataJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorldActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorldActivities_CharacterAssignments_CharacterAssignmentId",
                        column: x => x.CharacterAssignmentId,
                        principalTable: "CharacterAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorldActivities_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorldActivities_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorldActivities_Quests_QuestId",
                        column: x => x.QuestId,
                        principalTable: "Quests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorldActivities_GameId_TimestampUtc",
                table: "WorldActivities",
                columns: new[] { "GameId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WorldActivities_CharacterAssignmentId",
                table: "WorldActivities",
                column: "CharacterAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_WorldActivities_LocationId",
                table: "WorldActivities",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_WorldActivities_QuestId",
                table: "WorldActivities",
                column: "QuestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorldActivities");
        }
    }
}
