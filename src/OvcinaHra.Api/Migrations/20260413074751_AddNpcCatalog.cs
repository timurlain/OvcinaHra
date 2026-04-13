using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNpcCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Npcs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    ImagePath = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Npcs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameNpcs",
                columns: table => new
                {
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    NpcId = table.Column<int>(type: "integer", nullable: false),
                    PlayedByPersonId = table.Column<int>(type: "integer", nullable: true),
                    PlayedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PlayedByEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameNpcs", x => new { x.GameId, x.NpcId });
                    table.ForeignKey(
                        name: "FK_GameNpcs_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameNpcs_Npcs_NpcId",
                        column: x => x.NpcId,
                        principalTable: "Npcs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameNpcs_GameId",
                table: "GameNpcs",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_GameNpcs_NpcId",
                table: "GameNpcs",
                column: "NpcId");

            migrationBuilder.CreateIndex(
                name: "IX_GameNpcs_PlayedByEmail",
                table: "GameNpcs",
                column: "PlayedByEmail");

            migrationBuilder.CreateIndex(
                name: "IX_GameNpcs_PlayedByPersonId",
                table: "GameNpcs",
                column: "PlayedByPersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameNpcs");

            migrationBuilder.DropTable(
                name: "Npcs");
        }
    }
}
