using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Characters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Race = table.Column<string>(type: "text", nullable: true),
                    Class = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Kingdom = table.Column<string>(type: "text", nullable: true),
                    BirthYear = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    IsPlayedCharacter = table.Column<bool>(type: "boolean", nullable: false),
                    ExternalPersonId = table.Column<int>(type: "integer", nullable: true),
                    ParentCharacterId = table.Column<int>(type: "integer", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Characters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Characters_Characters_ParentCharacterId",
                        column: x => x.ParentCharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CharacterAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CharacterId = table.Column<int>(type: "integer", nullable: false),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    ExternalPersonId = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterAssignments_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CharacterEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CharacterAssignmentId = table.Column<int>(type: "integer", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OrganizerUserId = table.Column<string>(type: "text", nullable: false),
                    OrganizerName = table.Column<string>(type: "text", nullable: false),
                    EventType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Data = table.Column<string>(type: "text", nullable: false),
                    Location = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterEvents_CharacterAssignments_CharacterAssignmentId",
                        column: x => x.CharacterAssignmentId,
                        principalTable: "CharacterAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterAssignments_ExternalPersonId",
                table: "CharacterAssignments",
                column: "ExternalPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterAssignments_GameId",
                table: "CharacterAssignments",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterAssignments_CharacterId",
                table: "CharacterAssignments",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterEvents_CharacterAssignmentId",
                table: "CharacterEvents",
                column: "CharacterAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_ExternalPersonId",
                table: "Characters",
                column: "ExternalPersonId",
                unique: true,
                filter: "\"ExternalPersonId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_ParentCharacterId",
                table: "Characters",
                column: "ParentCharacterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterEvents");

            migrationBuilder.DropTable(
                name: "CharacterAssignments");

            migrationBuilder.DropTable(
                name: "Characters");
        }
    }
}
