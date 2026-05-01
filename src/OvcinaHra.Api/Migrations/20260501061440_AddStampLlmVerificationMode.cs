using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStampLlmVerificationMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GameId",
                table: "StampLlmVerifications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "StampLlmVerifications",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "verify");

            migrationBuilder.AddColumn<int>(
                name: "ReferencesScanned",
                table: "StampLlmVerifications",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StampLlmVerifications_Mode",
                table: "StampLlmVerifications",
                column: "Mode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StampLlmVerifications_Mode",
                table: "StampLlmVerifications");

            migrationBuilder.DropColumn(
                name: "GameId",
                table: "StampLlmVerifications");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "StampLlmVerifications");

            migrationBuilder.DropColumn(
                name: "ReferencesScanned",
                table: "StampLlmVerifications");
        }
    }
}
