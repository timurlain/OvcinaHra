using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class BuildingCraftingCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BuildingRecipes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    OutputBuildingId = table.Column<int>(type: "integer", nullable: false),
                    MoneyCost = table.Column<int>(type: "integer", nullable: true),
                    IngredientNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingRecipes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildingRecipes_Buildings_OutputBuildingId",
                        column: x => x.OutputBuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuildingRecipes_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BuildingRecipeIngredients",
                columns: table => new
                {
                    BuildingRecipeId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingRecipeIngredients", x => new { x.BuildingRecipeId, x.ItemId });
                    table.ForeignKey(
                        name: "FK_BuildingRecipeIngredients_BuildingRecipes_BuildingRecipeId",
                        column: x => x.BuildingRecipeId,
                        principalTable: "BuildingRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuildingRecipeIngredients_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BuildingRecipePrerequisites",
                columns: table => new
                {
                    BuildingRecipeId = table.Column<int>(type: "integer", nullable: false),
                    RequiredBuildingId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingRecipePrerequisites", x => new { x.BuildingRecipeId, x.RequiredBuildingId });
                    table.ForeignKey(
                        name: "FK_BuildingRecipePrerequisites_BuildingRecipes_BuildingRecipeId",
                        column: x => x.BuildingRecipeId,
                        principalTable: "BuildingRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuildingRecipePrerequisites_Buildings_RequiredBuildingId",
                        column: x => x.RequiredBuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BuildingRecipeSkillRequirements",
                columns: table => new
                {
                    BuildingRecipeId = table.Column<int>(type: "integer", nullable: false),
                    GameSkillId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingRecipeSkillRequirements", x => new { x.BuildingRecipeId, x.GameSkillId });
                    table.ForeignKey(
                        name: "FK_BuildingRecipeSkillRequirements_BuildingRecipes_BuildingRec~",
                        column: x => x.BuildingRecipeId,
                        principalTable: "BuildingRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuildingRecipeSkillRequirements_GameSkills_GameSkillId",
                        column: x => x.GameSkillId,
                        principalTable: "GameSkills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuildingRecipeIngredients_ItemId",
                table: "BuildingRecipeIngredients",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingRecipePrerequisites_RequiredBuildingId",
                table: "BuildingRecipePrerequisites",
                column: "RequiredBuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingRecipes_GameId",
                table: "BuildingRecipes",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingRecipes_OutputBuildingId",
                table: "BuildingRecipes",
                column: "OutputBuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingRecipeSkillRequirements_GameSkillId",
                table: "BuildingRecipeSkillRequirements",
                column: "GameSkillId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuildingRecipeIngredients");

            migrationBuilder.DropTable(
                name: "BuildingRecipePrerequisites");

            migrationBuilder.DropTable(
                name: "BuildingRecipeSkillRequirements");

            migrationBuilder.DropTable(
                name: "BuildingRecipes");
        }
    }
}
