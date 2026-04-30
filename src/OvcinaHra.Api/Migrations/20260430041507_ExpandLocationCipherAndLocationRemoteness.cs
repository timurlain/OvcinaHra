using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class ExpandLocationCipherAndLocationRemoteness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LocationCiphers_Quests_QuestId",
                table: "LocationCiphers");

            migrationBuilder.DropIndex(
                name: "IX_LocationCiphers_GameId_LocationId_SkillKey",
                table: "LocationCiphers");

            migrationBuilder.DropIndex(
                name: "IX_LocationCiphers_QuestId",
                table: "LocationCiphers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LocationCipher_MessageNormalized_MaxBySkill",
                table: "LocationCiphers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LocationCipher_MessageNormalized_NotEmpty",
                table: "LocationCiphers");

            migrationBuilder.RenameColumn(
                name: "SkillKey",
                table: "LocationCiphers",
                newName: "Skill");

            migrationBuilder.RenameColumn(
                name: "QuestId",
                table: "LocationCiphers",
                newName: "LinkedQuestId");

            migrationBuilder.RenameColumn(
                name: "MessageRaw",
                table: "LocationCiphers",
                newName: "RevealText");

            migrationBuilder.RenameColumn(
                name: "MessageNormalized",
                table: "LocationCiphers",
                newName: "CipherText");

            migrationBuilder.AddColumn<string>(
                name: "RemotenessNotes",
                table: "Locations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RemotenessScore",
                table: "Locations",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CipherText",
                table: "LocationCiphers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedAtUtc",
                table: "LocationCiphers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClaimedByCharacterId",
                table: "LocationCiphers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "LocationCiphers",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Info");

            migrationBuilder.AddColumn<bool>(
                name: "IsClaimed",
                table: "LocationCiphers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LibraryKeyword",
                table: "LocationCiphers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LibraryReward",
                table: "LocationCiphers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LinkedStashNumber",
                table: "LocationCiphers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrganizerNotes",
                table: "LocationCiphers",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tier",
                table: "LocationCiphers",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Micro");

            migrationBuilder.Sql(
                """
                UPDATE "LocationCiphers"
                SET "CipherText" = 'XOX' || "CipherText" || 'XOX'
                WHERE "CipherText" IS NOT NULL AND "CipherText" NOT LIKE 'XOX%XOX';
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Location_RemotenessScore_Range",
                table: "Locations",
                sql: "\"RemotenessScore\" IS NULL OR (\"RemotenessScore\" >= 0 AND \"RemotenessScore\" <= 9)");

            migrationBuilder.CreateIndex(
                name: "IX_LocationCiphers_ClaimedByCharacterId",
                table: "LocationCiphers",
                column: "ClaimedByCharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_LocationCiphers_GameId_LibraryKeyword",
                table: "LocationCiphers",
                columns: new[] { "GameId", "LibraryKeyword" },
                unique: true,
                filter: "\"LibraryKeyword\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LocationCiphers_GameId_LocationId_Skill",
                table: "LocationCiphers",
                columns: new[] { "GameId", "LocationId", "Skill" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocationCiphers_LinkedQuestId",
                table: "LocationCiphers",
                column: "LinkedQuestId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LocationCipher_CipherText_Wrapped",
                table: "LocationCiphers",
                sql: "\"CipherText\" IS NULL OR (\"CipherText\" = upper(\"CipherText\") AND \"CipherText\" LIKE 'XOX%XOX')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LocationCipher_ClaimedAt_WhenClaimed",
                table: "LocationCiphers",
                sql: "(\"IsClaimed\" = false AND \"ClaimedAtUtc\" IS NULL) OR (\"IsClaimed\" = true AND \"ClaimedAtUtc\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LocationCipher_Pytlik_HasStash",
                table: "LocationCiphers",
                sql: "\"ContentType\" <> 'Pytlik' OR \"LinkedStashNumber\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LocationCipher_RevealText_NotEmpty",
                table: "LocationCiphers",
                sql: "char_length(trim(\"RevealText\")) > 0");

            migrationBuilder.AddForeignKey(
                name: "FK_LocationCiphers_Characters_ClaimedByCharacterId",
                table: "LocationCiphers",
                column: "ClaimedByCharacterId",
                principalTable: "Characters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_LocationCiphers_Quests_LinkedQuestId",
                table: "LocationCiphers",
                column: "LinkedQuestId",
                principalTable: "Quests",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LocationCiphers_Characters_ClaimedByCharacterId",
                table: "LocationCiphers");

            migrationBuilder.DropForeignKey(
                name: "FK_LocationCiphers_Quests_LinkedQuestId",
                table: "LocationCiphers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Location_RemotenessScore_Range",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_LocationCiphers_ClaimedByCharacterId",
                table: "LocationCiphers");

            migrationBuilder.DropIndex(
                name: "IX_LocationCiphers_GameId_LibraryKeyword",
                table: "LocationCiphers");

            migrationBuilder.DropIndex(
                name: "IX_LocationCiphers_GameId_LocationId_Skill",
                table: "LocationCiphers");

            migrationBuilder.DropIndex(
                name: "IX_LocationCiphers_LinkedQuestId",
                table: "LocationCiphers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LocationCipher_CipherText_Wrapped",
                table: "LocationCiphers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LocationCipher_ClaimedAt_WhenClaimed",
                table: "LocationCiphers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LocationCipher_Pytlik_HasStash",
                table: "LocationCiphers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LocationCipher_RevealText_NotEmpty",
                table: "LocationCiphers");

            migrationBuilder.DropColumn(
                name: "RemotenessNotes",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "RemotenessScore",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "ClaimedAtUtc",
                table: "LocationCiphers");

            migrationBuilder.DropColumn(
                name: "ClaimedByCharacterId",
                table: "LocationCiphers");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "LocationCiphers");

            migrationBuilder.DropColumn(
                name: "IsClaimed",
                table: "LocationCiphers");

            migrationBuilder.DropColumn(
                name: "LibraryKeyword",
                table: "LocationCiphers");

            migrationBuilder.DropColumn(
                name: "LibraryReward",
                table: "LocationCiphers");

            migrationBuilder.DropColumn(
                name: "LinkedStashNumber",
                table: "LocationCiphers");

            migrationBuilder.DropColumn(
                name: "OrganizerNotes",
                table: "LocationCiphers");

            migrationBuilder.DropColumn(
                name: "Tier",
                table: "LocationCiphers");

            migrationBuilder.RenameColumn(
                name: "Skill",
                table: "LocationCiphers",
                newName: "SkillKey");

            migrationBuilder.RenameColumn(
                name: "RevealText",
                table: "LocationCiphers",
                newName: "MessageRaw");

            migrationBuilder.RenameColumn(
                name: "LinkedQuestId",
                table: "LocationCiphers",
                newName: "QuestId");

            migrationBuilder.Sql(
                """
                UPDATE "LocationCiphers"
                SET "CipherText" = COALESCE(NULLIF(regexp_replace("CipherText", '^XOX|XOX$', '', 'g'), ''), 'NEZNAMO');
                """);

            migrationBuilder.RenameColumn(
                name: "CipherText",
                table: "LocationCiphers",
                newName: "MessageNormalized");

            migrationBuilder.AlterColumn<string>(
                name: "MessageNormalized",
                table: "LocationCiphers",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocationCiphers_GameId_LocationId_SkillKey",
                table: "LocationCiphers",
                columns: new[] { "GameId", "LocationId", "SkillKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocationCiphers_QuestId",
                table: "LocationCiphers",
                column: "QuestId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LocationCipher_MessageNormalized_MaxBySkill",
                table: "LocationCiphers",
                sql: "(\"SkillKey\" = 'Lezeni' AND char_length(\"MessageNormalized\") <= 72) OR (\"SkillKey\" <> 'Lezeni' AND char_length(\"MessageNormalized\") <= 74)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LocationCipher_MessageNormalized_NotEmpty",
                table: "LocationCiphers",
                sql: "char_length(\"MessageNormalized\") > 0");

            migrationBuilder.AddForeignKey(
                name: "FK_LocationCiphers_Quests_QuestId",
                table: "LocationCiphers",
                column: "QuestId",
                principalTable: "Quests",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
