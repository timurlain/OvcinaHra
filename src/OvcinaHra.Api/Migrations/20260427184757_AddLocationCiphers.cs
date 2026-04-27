using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationCiphers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LocationCiphers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    SkillKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    MessageRaw = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    MessageNormalized = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    QuestId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationCiphers", x => x.Id);
                    table.CheckConstraint("CK_LocationCipher_MessageNormalized_MaxBySkill", "(\"SkillKey\" = 'Lezeni' AND char_length(\"MessageNormalized\") <= 72) OR (\"SkillKey\" <> 'Lezeni' AND char_length(\"MessageNormalized\") <= 74)");
                    table.CheckConstraint("CK_LocationCipher_MessageNormalized_NotEmpty", "char_length(\"MessageNormalized\") > 0");
                    table.ForeignKey(
                        name: "FK_LocationCiphers_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LocationCiphers_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LocationCiphers_Quests_QuestId",
                        column: x => x.QuestId,
                        principalTable: "Quests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LocationCiphers_GameId_LocationId",
                table: "LocationCiphers",
                columns: new[] { "GameId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_LocationCiphers_GameId_LocationId_SkillKey",
                table: "LocationCiphers",
                columns: new[] { "GameId", "LocationId", "SkillKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocationCiphers_LocationId",
                table: "LocationCiphers",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_LocationCiphers_QuestId",
                table: "LocationCiphers",
                column: "QuestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LocationCiphers");
        }
    }
}
