using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCostMoneyAndEffectToBuilding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CostMoney",
                table: "Buildings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Effect",
                table: "Buildings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CostMoney",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "Effect",
                table: "Buildings");
        }
    }
}
