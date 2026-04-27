using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTreasureQuestVerifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TreasureQuestVerifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TreasureQuestId = table.Column<int>(type: "integer", nullable: false),
                    CharacterAssignmentId = table.Column<int>(type: "integer", nullable: false),
                    CharacterEventId = table.Column<int>(type: "integer", nullable: false),
                    VerifiedStashId = table.Column<int>(type: "integer", nullable: false),
                    MatchConfidence = table.Column<double>(type: "double precision", nullable: true),
                    Override = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OrganizerUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OrganizerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreasureQuestVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TreasureQuestVerifications_CharacterAssignments_CharacterAs~",
                        column: x => x.CharacterAssignmentId,
                        principalTable: "CharacterAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TreasureQuestVerifications_CharacterEvents_CharacterEventId",
                        column: x => x.CharacterEventId,
                        principalTable: "CharacterEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TreasureQuestVerifications_SecretStashes_VerifiedStashId",
                        column: x => x.VerifiedStashId,
                        principalTable: "SecretStashes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreasureQuestVerifications_TreasureQuests_TreasureQuestId",
                        column: x => x.TreasureQuestId,
                        principalTable: "TreasureQuests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TreasureQuestVerifications_CharacterAssignmentId",
                table: "TreasureQuestVerifications",
                column: "CharacterAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TreasureQuestVerifications_CharacterEventId",
                table: "TreasureQuestVerifications",
                column: "CharacterEventId");

            migrationBuilder.CreateIndex(
                name: "IX_TreasureQuestVerifications_TreasureQuestId_CharacterAssignm~",
                table: "TreasureQuestVerifications",
                columns: new[] { "TreasureQuestId", "CharacterAssignmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreasureQuestVerifications_VerifiedStashId",
                table: "TreasureQuestVerifications",
                column: "VerifiedStashId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TreasureQuestVerifications");
        }
    }
}
