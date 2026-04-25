using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddItemNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "Items",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IngredientNotes",
                table: "CraftingRecipes",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Note",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "IngredientNotes",
                table: "CraftingRecipes");
        }
    }
}
