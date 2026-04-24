using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class ChangeMonsterCategoryToEnum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // int -> text with explicit CASE mapping. Legacy canonical data
            // only used 1..5 (verified against docs/legacy-data/monsters.json
            // at migration time), but the ELSE 'Tier1' branch keeps the
            // migration from crashing if prod has drifted to 0 or 6..10.
            migrationBuilder.Sql(@"
                ALTER TABLE ""Monsters""
                ALTER COLUMN ""Category"" TYPE character varying(20)
                USING CASE ""Category""
                    WHEN 1 THEN 'Tier1'
                    WHEN 2 THEN 'Tier2'
                    WHEN 3 THEN 'Tier3'
                    WHEN 4 THEN 'Tier4'
                    WHEN 5 THEN 'Tier5'
                    ELSE 'Tier1'
                END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""Monsters""
                ALTER COLUMN ""Category"" TYPE integer
                USING CASE ""Category""
                    WHEN 'Tier1' THEN 1
                    WHEN 'Tier2' THEN 2
                    WHEN 'Tier3' THEN 3
                    WHEN 'Tier4' THEN 4
                    WHEN 'Tier5' THEN 5
                    ELSE 1
                END;
            ");
        }
    }
}
