using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class RefactorCharacterPlayerNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Class",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "Kingdom",
                table: "Characters");

            migrationBuilder.AddColumn<string>(
                name: "PlayerFirstName",
                table: "Characters",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlayerLastName",
                table: "Characters",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Class",
                table: "CharacterAssignments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kingdom",
                table: "CharacterAssignments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RegistraceCharacterId",
                table: "CharacterAssignments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CharacterAssignments_GameId_ExternalPersonId",
                table: "CharacterAssignments",
                columns: new[] { "GameId", "ExternalPersonId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CharacterAssignments_GameId_ExternalPersonId",
                table: "CharacterAssignments");

            migrationBuilder.DropColumn(
                name: "PlayerFirstName",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "PlayerLastName",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "Class",
                table: "CharacterAssignments");

            migrationBuilder.DropColumn(
                name: "Kingdom",
                table: "CharacterAssignments");

            migrationBuilder.DropColumn(
                name: "RegistraceCharacterId",
                table: "CharacterAssignments");

            migrationBuilder.AddColumn<string>(
                name: "Class",
                table: "Characters",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kingdom",
                table: "Characters",
                type: "text",
                nullable: true);
        }
    }
}
