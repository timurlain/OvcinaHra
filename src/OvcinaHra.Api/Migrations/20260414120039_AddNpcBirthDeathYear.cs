using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNpcBirthDeathYear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BirthYear",
                table: "Npcs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeathYear",
                table: "Npcs",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BirthYear",
                table: "Npcs");

            migrationBuilder.DropColumn(
                name: "DeathYear",
                table: "Npcs");
        }
    }
}
