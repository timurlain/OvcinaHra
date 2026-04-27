using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEventIdempotencies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventIdempotencies",
                columns: table => new
                {
                    CharacterAssignmentId = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventIdempotencies", x => new { x.CharacterAssignmentId, x.IdempotencyKey });
                    table.ForeignKey(
                        name: "FK_EventIdempotencies_CharacterAssignments_CharacterAssignment~",
                        column: x => x.CharacterAssignmentId,
                        principalTable: "CharacterAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventIdempotencies_CharacterEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "CharacterEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventIdempotencies_CreatedAtUtc",
                table: "EventIdempotencies",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EventIdempotencies_EventId",
                table: "EventIdempotencies",
                column: "EventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventIdempotencies");
        }
    }
}
