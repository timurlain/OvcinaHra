using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizerRoleAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizerRoleAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    GameTimeSlotId = table.Column<int>(type: "integer", nullable: false),
                    NpcId = table.Column<int>(type: "integer", nullable: false),
                    PersonId = table.Column<int>(type: "integer", nullable: false),
                    PersonName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PersonEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizerRoleAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizerRoleAssignments_GameTimeSlots_GameTimeSlotId",
                        column: x => x.GameTimeSlotId,
                        principalTable: "GameTimeSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizerRoleAssignments_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizerRoleAssignments_Npcs_NpcId",
                        column: x => x.NpcId,
                        principalTable: "Npcs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizerRoleAssignments_GameId_GameTimeSlotId_NpcId",
                table: "OrganizerRoleAssignments",
                columns: new[] { "GameId", "GameTimeSlotId", "NpcId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizerRoleAssignments_GameId_PersonId",
                table: "OrganizerRoleAssignments",
                columns: new[] { "GameId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizerRoleAssignments_GameTimeSlotId",
                table: "OrganizerRoleAssignments",
                column: "GameTimeSlotId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizerRoleAssignments_NpcId",
                table: "OrganizerRoleAssignments",
                column: "NpcId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizerRoleAssignments");
        }
    }
}
