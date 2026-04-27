using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileGameItemIsSoldFromPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "GameItems"
                SET "IsSold" = (COALESCE("Price", 0) > 0)
                WHERE "IsSold" IS DISTINCT FROM (COALESCE("Price", 0) > 0);
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op for #305: legacy IsSold values cannot be reconstructed.
        }
    }
}
