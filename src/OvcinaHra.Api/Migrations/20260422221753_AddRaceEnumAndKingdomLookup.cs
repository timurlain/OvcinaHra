using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRaceEnumAndKingdomLookup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create Kingdoms lookup table.
            migrationBuilder.CreateTable(
                name: "Kingdoms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    HexColor = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    BadgeImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kingdoms", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Kingdoms_Name",
                table: "Kingdoms",
                column: "Name",
                unique: true);

            // 2. Seed the four canonical kingdoms (hex matches the UI
            //    --oh-kingdom-* tokens in app.css — do NOT re-saturate).
            migrationBuilder.Sql(@"
INSERT INTO ""Kingdoms"" (""Name"", ""HexColor"", ""SortOrder"") VALUES
  ('Aradhryand',      '#2E7D32', 1),
  ('Azanulinbar-Dum', '#C62828', 2),
  ('Esgaroth',        '#1565C0', 3),
  ('Nový Arnor',      '#F9A825', 4);
");

            // 3. Add nullable KingdomId on CharacterAssignments.
            migrationBuilder.AddColumn<int>(
                name: "KingdomId",
                table: "CharacterAssignments",
                type: "integer",
                nullable: true);

            // 4. Data migration: resolve existing free-text Kingdom strings
            //    against the lookup table (case-insensitive) before dropping
            //    the column. Unmatched values fall through as NULL.
            migrationBuilder.Sql(@"
UPDATE ""CharacterAssignments"" ca
SET ""KingdomId"" = k.""Id""
FROM ""Kingdoms"" k
WHERE ca.""Kingdom"" IS NOT NULL
  AND LOWER(k.""Name"") = LOWER(ca.""Kingdom"");
");

            // 5. Drop the old free-text column.
            migrationBuilder.DropColumn(
                name: "Kingdom",
                table: "CharacterAssignments");

            // 6. Migrate Character.Race: existing column is unbounded text with
            //    free-text Czech values. Normalize known values to the new
            //    Race enum names (English, stored as strings via HasConversion);
            //    unknown values become NULL so the UI prompts to re-pick.
            migrationBuilder.Sql(@"
UPDATE ""Characters""
SET ""Race"" = CASE LOWER(""Race"")
    WHEN 'human'     THEN 'Human'
    WHEN 'člověk'    THEN 'Human'
    WHEN 'clovek'    THEN 'Human'
    WHEN 'dwarf'     THEN 'Dwarf'
    WHEN 'trpaslík'  THEN 'Dwarf'
    WHEN 'trpaslik'  THEN 'Dwarf'
    WHEN 'elf'       THEN 'Elf'
    WHEN 'hobbit'    THEN 'Hobbit'
    WHEN 'hobit'     THEN 'Hobbit'
    WHEN 'dunedain'  THEN 'Dunedain'
    WHEN 'dúnadan'   THEN 'Dunedain'
    WHEN 'dunadan'   THEN 'Dunedain'
    ELSE NULL
END
WHERE ""Race"" IS NOT NULL;
");

            migrationBuilder.AlterColumn<string>(
                name: "Race",
                table: "Characters",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            // 7. Wire the FK + index now that KingdomId is populated.
            migrationBuilder.CreateIndex(
                name: "IX_CharacterAssignments_KingdomId",
                table: "CharacterAssignments",
                column: "KingdomId");

            migrationBuilder.AddForeignKey(
                name: "FK_CharacterAssignments_Kingdoms_KingdomId",
                table: "CharacterAssignments",
                column: "KingdomId",
                principalTable: "Kingdoms",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CharacterAssignments_Kingdoms_KingdomId",
                table: "CharacterAssignments");

            migrationBuilder.DropIndex(
                name: "IX_CharacterAssignments_KingdomId",
                table: "CharacterAssignments");

            migrationBuilder.AlterColumn<string>(
                name: "Race",
                table: "Characters",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kingdom",
                table: "CharacterAssignments",
                type: "text",
                nullable: true);

            // Reverse data migration: copy Kingdom.Name back to the free-text
            // column so rollback preserves visible values.
            migrationBuilder.Sql(@"
UPDATE ""CharacterAssignments"" ca
SET ""Kingdom"" = k.""Name""
FROM ""Kingdoms"" k
WHERE ca.""KingdomId"" = k.""Id"";
");

            migrationBuilder.DropColumn(
                name: "KingdomId",
                table: "CharacterAssignments");

            migrationBuilder.DropTable(
                name: "Kingdoms");
        }
    }
}
