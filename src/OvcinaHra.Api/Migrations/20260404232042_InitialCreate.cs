using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Edition = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ImagePath = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ItemType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Effect = table.Column<string>(type: "text", nullable: true),
                    PhysicalForm = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    IsCraftable = table.Column<bool>(type: "boolean", nullable: false),
                    req_warrior = table.Column<int>(type: "integer", nullable: false),
                    req_archer = table.Column<int>(type: "integer", nullable: false),
                    req_mage = table.Column<int>(type: "integer", nullable: false),
                    req_thief = table.Column<int>(type: "integer", nullable: false),
                    IsUnique = table.Column<bool>(type: "boolean", nullable: false),
                    IsLimited = table.Column<bool>(type: "boolean", nullable: false),
                    ImagePath = table.Column<string>(type: "text", nullable: true),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple', coalesce(\"Name\", '') || ' ' || coalesce(\"Effect\", ''))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    LocationKind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: false),
                    longitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: false),
                    ImagePath = table.Column<string>(type: "text", nullable: true),
                    PlacementPhotoPath = table.Column<string>(type: "text", nullable: true),
                    NpcInfo = table.Column<string>(type: "text", nullable: true),
                    SetupNotes = table.Column<string>(type: "text", nullable: true),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple', coalesce(\"Name\", '') || ' ' || coalesce(\"Description\", '') || ' ' || coalesce(\"NpcInfo\", '') || ' ' || coalesce(\"SetupNotes\", ''))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Monsters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    MonsterType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Abilities = table.Column<string>(type: "text", nullable: true),
                    AiBehavior = table.Column<string>(type: "text", nullable: true),
                    stat_attack = table.Column<int>(type: "integer", nullable: false),
                    stat_defense = table.Column<int>(type: "integer", nullable: false),
                    stat_health = table.Column<int>(type: "integer", nullable: false),
                    RewardXp = table.Column<int>(type: "integer", nullable: true),
                    RewardMoney = table.Column<int>(type: "integer", nullable: true),
                    RewardNotes = table.Column<string>(type: "text", nullable: true),
                    ImagePath = table.Column<string>(type: "text", nullable: true),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple', coalesce(\"Name\", '') || ' ' || coalesce(\"Abilities\", '') || ' ' || coalesce(\"AiBehavior\", ''))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Monsters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BattlefieldBonuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AttackBonus = table.Column<int>(type: "integer", nullable: false),
                    DefenseBonus = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ImagePath = table.Column<string>(type: "text", nullable: true),
                    GameId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BattlefieldBonuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BattlefieldBonuses_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Quests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    QuestType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    FullText = table.Column<string>(type: "text", nullable: true),
                    TimeSlot = table.Column<string>(type: "text", nullable: true),
                    RewardXp = table.Column<int>(type: "integer", nullable: true),
                    RewardMoney = table.Column<int>(type: "integer", nullable: true),
                    RewardNotes = table.Column<string>(type: "text", nullable: true),
                    ChainOrder = table.Column<int>(type: "integer", nullable: true),
                    ParentQuestId = table.Column<int>(type: "integer", nullable: true),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple', coalesce(\"Name\", '') || ' ' || coalesce(\"Description\", '') || ' ' || coalesce(\"FullText\", ''))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Quests_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Quests_Quests_ParentQuestId",
                        column: x => x.ParentQuestId,
                        principalTable: "Quests",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GameItems",
                columns: table => new
                {
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<int>(type: "integer", nullable: true),
                    StockCount = table.Column<int>(type: "integer", nullable: true),
                    IsSold = table.Column<bool>(type: "boolean", nullable: false),
                    SaleCondition = table.Column<string>(type: "text", nullable: true),
                    IsFindable = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameItems", x => new { x.GameId, x.ItemId });
                    table.ForeignKey(
                        name: "FK_GameItems_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameItems_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Buildings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ImagePath = table.Column<string>(type: "text", nullable: true),
                    LocationId = table.Column<int>(type: "integer", nullable: true),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    IsPrebuilt = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Buildings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Buildings_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Buildings_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CraftingRecipes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    OutputItemId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CraftingRecipes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CraftingRecipes_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CraftingRecipes_Items_OutputItemId",
                        column: x => x.OutputItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CraftingRecipes_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GameLocations",
                columns: table => new
                {
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameLocations", x => new { x.GameId, x.LocationId });
                    table.ForeignKey(
                        name: "FK_GameLocations_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameLocations_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SecretStashes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ImagePath = table.Column<string>(type: "text", nullable: true),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    GameId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecretStashes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecretStashes_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SecretStashes_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MonsterLoots",
                columns: table => new
                {
                    MonsterId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonsterLoots", x => new { x.MonsterId, x.ItemId, x.GameId });
                    table.ForeignKey(
                        name: "FK_MonsterLoots_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MonsterLoots_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MonsterLoots_Monsters_MonsterId",
                        column: x => x.MonsterId,
                        principalTable: "Monsters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MonsterTagLinks",
                columns: table => new
                {
                    MonsterId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonsterTagLinks", x => new { x.MonsterId, x.TagId });
                    table.ForeignKey(
                        name: "FK_MonsterTagLinks_Monsters_MonsterId",
                        column: x => x.MonsterId,
                        principalTable: "Monsters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MonsterTagLinks_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameTimeSlots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InGameYear = table.Column<int>(type: "integer", nullable: true),
                    StartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    Rules = table.Column<string>(type: "text", nullable: true),
                    BattlefieldBonusId = table.Column<int>(type: "integer", nullable: true),
                    GameId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameTimeSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameTimeSlots_BattlefieldBonuses_BattlefieldBonusId",
                        column: x => x.BattlefieldBonusId,
                        principalTable: "BattlefieldBonuses",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GameTimeSlots_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuestEncounters",
                columns: table => new
                {
                    QuestId = table.Column<int>(type: "integer", nullable: false),
                    MonsterId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestEncounters", x => new { x.QuestId, x.MonsterId });
                    table.ForeignKey(
                        name: "FK_QuestEncounters_Monsters_MonsterId",
                        column: x => x.MonsterId,
                        principalTable: "Monsters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuestEncounters_Quests_QuestId",
                        column: x => x.QuestId,
                        principalTable: "Quests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuestLocationLinks",
                columns: table => new
                {
                    QuestId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestLocationLinks", x => new { x.QuestId, x.LocationId });
                    table.ForeignKey(
                        name: "FK_QuestLocationLinks_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuestLocationLinks_Quests_QuestId",
                        column: x => x.QuestId,
                        principalTable: "Quests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuestRewards",
                columns: table => new
                {
                    QuestId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestRewards", x => new { x.QuestId, x.ItemId });
                    table.ForeignKey(
                        name: "FK_QuestRewards_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuestRewards_Quests_QuestId",
                        column: x => x.QuestId,
                        principalTable: "Quests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuestTagLinks",
                columns: table => new
                {
                    QuestId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestTagLinks", x => new { x.QuestId, x.TagId });
                    table.ForeignKey(
                        name: "FK_QuestTagLinks_Quests_QuestId",
                        column: x => x.QuestId,
                        principalTable: "Quests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuestTagLinks_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CraftingBuildingRequirements",
                columns: table => new
                {
                    CraftingRecipeId = table.Column<int>(type: "integer", nullable: false),
                    BuildingId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CraftingBuildingRequirements", x => new { x.CraftingRecipeId, x.BuildingId });
                    table.ForeignKey(
                        name: "FK_CraftingBuildingRequirements_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CraftingBuildingRequirements_CraftingRecipes_CraftingRecipe~",
                        column: x => x.CraftingRecipeId,
                        principalTable: "CraftingRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CraftingIngredients",
                columns: table => new
                {
                    CraftingRecipeId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CraftingIngredients", x => new { x.CraftingRecipeId, x.ItemId });
                    table.ForeignKey(
                        name: "FK_CraftingIngredients_CraftingRecipes_CraftingRecipeId",
                        column: x => x.CraftingRecipeId,
                        principalTable: "CraftingRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CraftingIngredients_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TreasureQuests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Clue = table.Column<string>(type: "text", nullable: true),
                    Difficulty = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: true),
                    SecretStashId = table.Column<int>(type: "integer", nullable: true),
                    GameId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreasureQuests", x => x.Id);
                    table.CheckConstraint("CK_TreasureQuest_LocationOrStash", "(\"LocationId\" IS NOT NULL) <> (\"SecretStashId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TreasureQuests_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TreasureQuests_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TreasureQuests_SecretStashes_SecretStashId",
                        column: x => x.SecretStashId,
                        principalTable: "SecretStashes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TreasureItems",
                columns: table => new
                {
                    TreasureQuestId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreasureItems", x => new { x.TreasureQuestId, x.ItemId });
                    table.ForeignKey(
                        name: "FK_TreasureItems_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TreasureItems_TreasureQuests_TreasureQuestId",
                        column: x => x.TreasureQuestId,
                        principalTable: "TreasureQuests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BattlefieldBonuses_GameId",
                table: "BattlefieldBonuses",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_GameId",
                table: "Buildings",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_LocationId",
                table: "Buildings",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_CraftingBuildingRequirements_BuildingId",
                table: "CraftingBuildingRequirements",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_CraftingIngredients_ItemId",
                table: "CraftingIngredients",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CraftingRecipes_GameId",
                table: "CraftingRecipes",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_CraftingRecipes_LocationId",
                table: "CraftingRecipes",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_CraftingRecipes_OutputItemId",
                table: "CraftingRecipes",
                column: "OutputItemId");

            migrationBuilder.CreateIndex(
                name: "IX_GameItems_GameId",
                table: "GameItems",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_GameItems_ItemId",
                table: "GameItems",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_GameLocations_GameId",
                table: "GameLocations",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_GameLocations_LocationId",
                table: "GameLocations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_GameTimeSlots_BattlefieldBonusId",
                table: "GameTimeSlots",
                column: "BattlefieldBonusId");

            migrationBuilder.CreateIndex(
                name: "IX_GameTimeSlots_GameId",
                table: "GameTimeSlots",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_Name",
                table: "Items",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Items_SearchVector",
                table: "Items",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_Name",
                table: "Locations",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_SearchVector",
                table: "Locations",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_MonsterLoots_GameId",
                table: "MonsterLoots",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_MonsterLoots_ItemId",
                table: "MonsterLoots",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Monsters_Name",
                table: "Monsters",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Monsters_SearchVector",
                table: "Monsters",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_MonsterTagLinks_TagId",
                table: "MonsterTagLinks",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestEncounters_MonsterId",
                table: "QuestEncounters",
                column: "MonsterId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestLocationLinks_LocationId",
                table: "QuestLocationLinks",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestRewards_ItemId",
                table: "QuestRewards",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Quests_GameId",
                table: "Quests",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_Quests_ParentQuestId",
                table: "Quests",
                column: "ParentQuestId");

            migrationBuilder.CreateIndex(
                name: "IX_Quests_SearchVector",
                table: "Quests",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_QuestTagLinks_TagId",
                table: "QuestTagLinks",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_SecretStashes_GameId_Name",
                table: "SecretStashes",
                columns: new[] { "GameId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecretStashes_LocationId",
                table: "SecretStashes",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Kind_Name",
                table: "Tags",
                columns: new[] { "Kind", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreasureItems_ItemId",
                table: "TreasureItems",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_TreasureQuests_GameId",
                table: "TreasureQuests",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_TreasureQuests_LocationId",
                table: "TreasureQuests",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_TreasureQuests_SecretStashId",
                table: "TreasureQuests",
                column: "SecretStashId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CraftingBuildingRequirements");

            migrationBuilder.DropTable(
                name: "CraftingIngredients");

            migrationBuilder.DropTable(
                name: "GameItems");

            migrationBuilder.DropTable(
                name: "GameLocations");

            migrationBuilder.DropTable(
                name: "GameTimeSlots");

            migrationBuilder.DropTable(
                name: "MonsterLoots");

            migrationBuilder.DropTable(
                name: "MonsterTagLinks");

            migrationBuilder.DropTable(
                name: "QuestEncounters");

            migrationBuilder.DropTable(
                name: "QuestLocationLinks");

            migrationBuilder.DropTable(
                name: "QuestRewards");

            migrationBuilder.DropTable(
                name: "QuestTagLinks");

            migrationBuilder.DropTable(
                name: "TreasureItems");

            migrationBuilder.DropTable(
                name: "Buildings");

            migrationBuilder.DropTable(
                name: "CraftingRecipes");

            migrationBuilder.DropTable(
                name: "BattlefieldBonuses");

            migrationBuilder.DropTable(
                name: "Monsters");

            migrationBuilder.DropTable(
                name: "Quests");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "TreasureQuests");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.DropTable(
                name: "SecretStashes");

            migrationBuilder.DropTable(
                name: "Games");

            migrationBuilder.DropTable(
                name: "Locations");
        }
    }
}
