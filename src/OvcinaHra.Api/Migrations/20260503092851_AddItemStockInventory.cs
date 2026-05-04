using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddItemStockInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StockCount",
                table: "Items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StockNote",
                table: "Items",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StockUpdatedBy",
                table: "Items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StockUpdatedUtc",
                table: "Items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Item_StockCount_NonNegative",
                table: "Items",
                sql: "\"StockCount\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Item_StockCount_NonNegative",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "StockCount",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "StockNote",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "StockUpdatedBy",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "StockUpdatedUtc",
                table: "Items");
        }
    }
}
