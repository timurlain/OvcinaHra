using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipeTemplateForkNameAndCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CraftingRecipes_Games_GameId",
                table: "CraftingRecipes");

            migrationBuilder.AlterColumn<int>(
                name: "GameId",
                table: "CraftingRecipes",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "CraftingRecipes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Ostatni");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "CraftingRecipes",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TemplateRecipeId",
                table: "CraftingRecipes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CraftingRecipes_TemplateRecipeId",
                table: "CraftingRecipes",
                column: "TemplateRecipeId");

            migrationBuilder.AddForeignKey(
                name: "FK_CraftingRecipes_CraftingRecipes_TemplateRecipeId",
                table: "CraftingRecipes",
                column: "TemplateRecipeId",
                principalTable: "CraftingRecipes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_CraftingRecipes_Games_GameId",
                table: "CraftingRecipes",
                column: "GameId",
                principalTable: "Games",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CraftingRecipes_CraftingRecipes_TemplateRecipeId",
                table: "CraftingRecipes");

            migrationBuilder.DropForeignKey(
                name: "FK_CraftingRecipes_Games_GameId",
                table: "CraftingRecipes");

            migrationBuilder.DropIndex(
                name: "IX_CraftingRecipes_TemplateRecipeId",
                table: "CraftingRecipes");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "CraftingRecipes");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "CraftingRecipes");

            migrationBuilder.DropColumn(
                name: "TemplateRecipeId",
                table: "CraftingRecipes");

            migrationBuilder.AlterColumn<int>(
                name: "GameId",
                table: "CraftingRecipes",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CraftingRecipes_Games_GameId",
                table: "CraftingRecipes",
                column: "GameId",
                principalTable: "Games",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
