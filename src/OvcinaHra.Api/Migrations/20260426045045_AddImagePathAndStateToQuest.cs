using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddImagePathAndStateToQuest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImagePath",
                table: "Quests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "Quests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Inactive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImagePath",
                table: "Quests");

            migrationBuilder.DropColumn(
                name: "State",
                table: "Quests");
        }
    }
}
