using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class SkillsAndPersonalQuestSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PersonalQuests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Difficulty = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AllowWarrior = table.Column<bool>(type: "boolean", nullable: false),
                    AllowArcher = table.Column<bool>(type: "boolean", nullable: false),
                    AllowMage = table.Column<bool>(type: "boolean", nullable: false),
                    AllowThief = table.Column<bool>(type: "boolean", nullable: false),
                    QuestCardText = table.Column<string>(type: "text", nullable: true),
                    RewardCardText = table.Column<string>(type: "text", nullable: true),
                    RewardNote = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    ImagePath = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalQuests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Skills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ClassRestriction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Effect = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RequirementNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ImagePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Skills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GamePersonalQuests",
                columns: table => new
                {
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    PersonalQuestId = table.Column<int>(type: "integer", nullable: false),
                    XpCost = table.Column<int>(type: "integer", nullable: false),
                    PerKingdomLimit = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GamePersonalQuests", x => new { x.GameId, x.PersonalQuestId });
                    table.CheckConstraint("CK_GamePersonalQuest_PKL_Positive", "\"PerKingdomLimit\" IS NULL OR \"PerKingdomLimit\" >= 1");
                    table.CheckConstraint("CK_GamePersonalQuest_XpCost_NonNegative", "\"XpCost\" >= 0");
                    table.ForeignKey(
                        name: "FK_GamePersonalQuests_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GamePersonalQuests_PersonalQuests_PersonalQuestId",
                        column: x => x.PersonalQuestId,
                        principalTable: "PersonalQuests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CharacterPersonalQuests",
                columns: table => new
                {
                    CharacterId = table.Column<int>(type: "integer", nullable: false),
                    PersonalQuestId = table.Column<int>(type: "integer", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterPersonalQuests", x => x.CharacterId);
                    table.ForeignKey(
                        name: "FK_CharacterPersonalQuests_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CharacterPersonalQuests_PersonalQuests_PersonalQuestId",
                        column: x => x.PersonalQuestId,
                        principalTable: "PersonalQuests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PersonalQuestItemRewards",
                columns: table => new
                {
                    PersonalQuestId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalQuestItemRewards", x => new { x.PersonalQuestId, x.ItemId });
                    table.CheckConstraint("CK_PQItemReward_Qty_Positive", "\"Quantity\" >= 1");
                    table.ForeignKey(
                        name: "FK_PersonalQuestItemRewards_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonalQuestItemRewards_PersonalQuests_PersonalQuestId",
                        column: x => x.PersonalQuestId,
                        principalTable: "PersonalQuests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameSkills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    TemplateSkillId = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ClassRestriction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Effect = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RequirementNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ImagePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    XpCost = table.Column<int>(type: "integer", nullable: false),
                    LevelRequirement = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSkills", x => x.Id);
                    table.CheckConstraint("CK_GameSkill_LevelRequirement_NonNegative", "\"LevelRequirement\" IS NULL OR \"LevelRequirement\" >= 0");
                    table.CheckConstraint("CK_GameSkill_XpCost_NonNegative", "\"XpCost\" >= 0");
                    table.ForeignKey(
                        name: "FK_GameSkills_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameSkills_Skills_TemplateSkillId",
                        column: x => x.TemplateSkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PersonalQuestSkillRewards",
                columns: table => new
                {
                    PersonalQuestId = table.Column<int>(type: "integer", nullable: false),
                    SkillId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalQuestSkillRewards", x => new { x.PersonalQuestId, x.SkillId });
                    table.ForeignKey(
                        name: "FK_PersonalQuestSkillRewards_PersonalQuests_PersonalQuestId",
                        column: x => x.PersonalQuestId,
                        principalTable: "PersonalQuests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PersonalQuestSkillRewards_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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

            migrationBuilder.CreateTable(
                name: "CraftingSkillRequirements",
                columns: table => new
                {
                    CraftingRecipeId = table.Column<int>(type: "integer", nullable: false),
                    GameSkillId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CraftingSkillRequirements", x => new { x.CraftingRecipeId, x.GameSkillId });
                    table.ForeignKey(
                        name: "FK_CraftingSkillRequirements_CraftingRecipes_CraftingRecipeId",
                        column: x => x.CraftingRecipeId,
                        principalTable: "CraftingRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CraftingSkillRequirements_GameSkills_GameSkillId",
                        column: x => x.GameSkillId,
                        principalTable: "GameSkills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GameSkillBuildingRequirements",
                columns: table => new
                {
                    GameSkillId = table.Column<int>(type: "integer", nullable: false),
                    BuildingId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSkillBuildingRequirements", x => new { x.GameSkillId, x.BuildingId });
                    table.ForeignKey(
                        name: "FK_GameSkillBuildingRequirements_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameSkillBuildingRequirements_GameSkills_GameSkillId",
                        column: x => x.GameSkillId,
                        principalTable: "GameSkills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CraftingSkillRequirements_GameSkillId",
                table: "CraftingSkillRequirements",
                column: "GameSkillId");

            migrationBuilder.CreateIndex(
                name: "IX_GamePersonalQuests_GameId",
                table: "GamePersonalQuests",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_GamePersonalQuests_PersonalQuestId",
                table: "GamePersonalQuests",
                column: "PersonalQuestId");

            migrationBuilder.CreateIndex(
                name: "IX_GameSkillBuildingRequirements_BuildingId",
                table: "GameSkillBuildingRequirements",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_GameSkills_GameId_Name",
                table: "GameSkills",
                columns: new[] { "GameId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameSkills_GameId_TemplateSkillId",
                table: "GameSkills",
                columns: new[] { "GameId", "TemplateSkillId" },
                unique: true,
                filter: "\"TemplateSkillId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GameSkills_TemplateSkillId",
                table: "GameSkills",
                column: "TemplateSkillId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterPersonalQuests_PersonalQuestId",
                table: "CharacterPersonalQuests",
                column: "PersonalQuestId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonalQuestItemRewards_ItemId",
                table: "PersonalQuestItemRewards",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonalQuests_Name",
                table: "PersonalQuests",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonalQuestSkillRewards_SkillId",
                table: "PersonalQuestSkillRewards",
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
                name: "GamePersonalQuests");

            migrationBuilder.DropTable(
                name: "GameSkillBuildingRequirements");

            migrationBuilder.DropTable(
                name: "CharacterPersonalQuests");

            migrationBuilder.DropTable(
                name: "PersonalQuestItemRewards");

            migrationBuilder.DropTable(
                name: "PersonalQuestSkillRewards");

            migrationBuilder.DropTable(
                name: "SkillBuildingRequirements");

            migrationBuilder.DropTable(
                name: "GameSkills");

            migrationBuilder.DropTable(
                name: "PersonalQuests");

            migrationBuilder.DropTable(
                name: "Skills");
        }
    }
}
