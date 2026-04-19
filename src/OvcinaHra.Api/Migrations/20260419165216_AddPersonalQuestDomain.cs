using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalQuestDomain : Migration
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

            migrationBuilder.CreateIndex(
                name: "IX_GamePersonalQuests_GameId",
                table: "GamePersonalQuests",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_GamePersonalQuests_PersonalQuestId",
                table: "GamePersonalQuests",
                column: "PersonalQuestId");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GamePersonalQuests");

            migrationBuilder.DropTable(
                name: "CharacterPersonalQuests");

            migrationBuilder.DropTable(
                name: "PersonalQuestItemRewards");

            migrationBuilder.DropTable(
                name: "PersonalQuestSkillRewards");

            migrationBuilder.DropTable(
                name: "PersonalQuests");
        }
    }
}
