using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGameBoundingBox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BoundingBoxNeLat",
                table: "Games",
                type: "numeric(10,7)",
                precision: 10,
                scale: 7,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BoundingBoxNeLng",
                table: "Games",
                type: "numeric(10,7)",
                precision: 10,
                scale: 7,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BoundingBoxSwLat",
                table: "Games",
                type: "numeric(10,7)",
                precision: 10,
                scale: 7,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BoundingBoxSwLng",
                table: "Games",
                type: "numeric(10,7)",
                precision: 10,
                scale: 7,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoundingBoxNeLat",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "BoundingBoxNeLng",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "BoundingBoxSwLat",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "BoundingBoxSwLng",
                table: "Games");
        }
    }
}
