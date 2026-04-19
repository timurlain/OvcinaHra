using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSkillCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Effect",
                table: "Skills",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Skills",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Class");

            // Backfill: preserve the implicit two-state distinction that existed before the enum.
            // Skills with a ClassRestriction stay Class; Skills without one become Adventure.
            migrationBuilder.Sql(
                "UPDATE \"Skills\" SET \"Category\" = 'Adventure' WHERE \"ClassRestriction\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "Skills");

            migrationBuilder.AlterColumn<string>(
                name: "Effect",
                table: "Skills",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);
        }
    }
}
