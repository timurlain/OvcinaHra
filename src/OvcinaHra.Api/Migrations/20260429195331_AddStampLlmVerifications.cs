using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStampLlmVerifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StampLlmVerifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OrganizerUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OrganizerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    ContextStashId = table.Column<int>(type: "integer", nullable: true),
                    ContextQuestId = table.Column<int>(type: "integer", nullable: true),
                    Match = table.Column<bool>(type: "boolean", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false),
                    RawResponse = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StampLlmVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StampLlmVerifications_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StampLlmVerifications_LocationId",
                table: "StampLlmVerifications",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_StampLlmVerifications_TimestampUtc",
                table: "StampLlmVerifications",
                column: "TimestampUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StampLlmVerifications");
        }
    }
}
