using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSkillsDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Skills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ClassRestriction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Effect = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RequirementNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ImagePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Skills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CraftingSkillRequirements",
                columns: table => new
                {
                    CraftingRecipeId = table.Column<int>(type: "integer", nullable: false),
                    SkillId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CraftingSkillRequirements", x => new { x.CraftingRecipeId, x.SkillId });
                    table.ForeignKey(
                        name: "FK_CraftingSkillRequirements_CraftingRecipes_CraftingRecipeId",
                        column: x => x.CraftingRecipeId,
                        principalTable: "CraftingRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CraftingSkillRequirements_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameSkills",
                columns: table => new
                {
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    SkillId = table.Column<int>(type: "integer", nullable: false),
                    XpCost = table.Column<int>(type: "integer", nullable: false),
                    LevelRequirement = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSkills", x => new { x.GameId, x.SkillId });
                    table.CheckConstraint("CK_GameSkill_LevelRequirement_NonNegative", "\"LevelRequirement\" IS NULL OR \"LevelRequirement\" >= 0");
                    table.CheckConstraint("CK_GameSkill_XpCost_NonNegative", "\"XpCost\" >= 0");
                    table.ForeignKey(
                        name: "FK_GameSkills_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameSkills_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SkillBuildingRequirements",
                columns: table => new
                {
                    SkillId = table.Column<int>(type: "integer", nullable: false),
                    BuildingId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkillBuildingRequirements", x => new { x.SkillId, x.BuildingId });
                    table.ForeignKey(
                        name: "FK_SkillBuildingRequirements_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SkillBuildingRequirements_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CraftingSkillRequirements_SkillId",
                table: "CraftingSkillRequirements",
                column: "SkillId");

            migrationBuilder.CreateIndex(
                name: "IX_GameSkills_GameId",
                table: "GameSkills",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_GameSkills_SkillId",
                table: "GameSkills",
                column: "SkillId");

            migrationBuilder.CreateIndex(
                name: "IX_SkillBuildingRequirements_BuildingId",
                table: "SkillBuildingRequirements",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_Skills_Name",
                table: "Skills",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CraftingSkillRequirements");

            migrationBuilder.DropTable(
                name: "GameSkills");

            migrationBuilder.DropTable(
                name: "SkillBuildingRequirements");

            migrationBuilder.DropTable(
                name: "Skills");
        }
    }
}
