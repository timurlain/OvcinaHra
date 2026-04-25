using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddScrollItemIdToSpell : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ScrollItemId",
                table: "Spells",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Spells_ScrollItemId",
                table: "Spells",
                column: "ScrollItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_Spells_Items_ScrollItemId",
                table: "Spells",
                column: "ScrollItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Issue #181 — one-shot backfill. The previous fuzz-match logic in
            // SpellDetail.razor matched a scroll spell to an Item by
            // case-insensitive Name. Replicate that join-set as an UPDATE so
            // existing data picks up the FK without a manual organizer pass.
            // Idempotent (the WHERE ScrollItemId IS NULL clause makes a
            // partially-applied migration safe to re-run). LOWER(...) on both
            // sides matches the previous string.Equals(..., OrdinalIgnoreCase)
            // semantics. No Down() inverse — reverting the migration drops
            // the column and the data with it.
            migrationBuilder.Sql(@"
                UPDATE ""Spells"" s
                SET ""ScrollItemId"" = i.""Id""
                FROM ""Items"" i
                WHERE s.""IsScroll"" = true
                  AND LOWER(i.""Name"") = LOWER(s.""Name"")
                  AND s.""ScrollItemId"" IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Spells_Items_ScrollItemId",
                table: "Spells");

            migrationBuilder.DropIndex(
                name: "IX_Spells_ScrollItemId",
                table: "Spells");

            migrationBuilder.DropColumn(
                name: "ScrollItemId",
                table: "Spells");
        }
    }
}
