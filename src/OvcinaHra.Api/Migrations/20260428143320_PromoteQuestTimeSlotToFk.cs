using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OvcinaHra.Api.Migrations
{
    /// <inheritdoc />
    public partial class PromoteQuestTimeSlotToFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TimeSlotId",
                table: "Quests",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("""
                WITH candidate_matches AS (
                    SELECT
                        q."Id" AS "QuestId",
                        s."Id" AS "TimeSlotId",
                        COUNT(*) OVER (PARTITION BY q."Id") AS "MatchCount"
                    FROM "Quests" q
                    JOIN "GameTimeSlots" s ON s."GameId" = q."GameId"
                    WHERE q."TimeSlot" IS NOT NULL
                        AND btrim(q."TimeSlot") <> ''
                        AND q."TimeSlot" = (
                            CASE s."Stage"
                                WHEN 'Start' THEN 'Start'
                                WHEN 'Early' THEN 'Rozvoj hry'
                                WHEN 'Midgame' THEN 'Střed hry'
                                WHEN 'Lategame' THEN 'Závěr hry'
                                WHEN 'EndGame' THEN 'Konec hry'
                                ELSE s."Stage"
                            END
                            || ': '
                            || CASE
                                WHEN s."InGameYear" IS NULL THEN ''
                                ELSE 'Rok ' || s."InGameYear"::text || ', '
                            END
                            || EXTRACT(DAY FROM (s."StartTime" AT TIME ZONE 'Europe/Prague'))::int::text
                            || '.'
                            || EXTRACT(MONTH FROM (s."StartTime" AT TIME ZONE 'Europe/Prague'))::int::text
                            || '. '
                            || to_char(s."StartTime" AT TIME ZONE 'Europe/Prague', 'HH24:MI')
                            || ' ('
                            || regexp_replace(
                                to_char(round((EXTRACT(EPOCH FROM s."Duration") / 3600.0)::numeric, 1), 'FM999999990.0'),
                                '\.0$',
                                '')
                            || ' h)'
                        )
                )
                UPDATE "Quests" q
                SET "TimeSlotId" = cm."TimeSlotId"
                FROM candidate_matches cm
                WHERE q."Id" = cm."QuestId"
                    AND cm."MatchCount" = 1;
                """);

            migrationBuilder.Sql("""
                DO $$
                DECLARE unmatched_count integer;
                BEGIN
                    SELECT COUNT(*)
                    INTO unmatched_count
                    FROM "Quests"
                    WHERE "TimeSlot" IS NOT NULL
                        AND btrim("TimeSlot") <> ''
                        AND "TimeSlotId" IS NULL;

                    RAISE NOTICE 'PromoteQuestTimeSlotToFk left % unmatched legacy TimeSlot values as NULL.', unmatched_count;
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "TimeSlot",
                table: "Quests");

            migrationBuilder.CreateIndex(
                name: "IX_Quests_TimeSlotId",
                table: "Quests",
                column: "TimeSlotId");

            migrationBuilder.AddForeignKey(
                name: "FK_Quests_GameTimeSlots_TimeSlotId",
                table: "Quests",
                column: "TimeSlotId",
                principalTable: "GameTimeSlots",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Quests_GameTimeSlots_TimeSlotId",
                table: "Quests");

            migrationBuilder.DropIndex(
                name: "IX_Quests_TimeSlotId",
                table: "Quests");

            migrationBuilder.AddColumn<string>(
                name: "TimeSlot",
                table: "Quests",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Quests" q
                SET "TimeSlot" = (
                    CASE s."Stage"
                        WHEN 'Start' THEN 'Start'
                        WHEN 'Early' THEN 'Rozvoj hry'
                        WHEN 'Midgame' THEN 'Střed hry'
                        WHEN 'Lategame' THEN 'Závěr hry'
                        WHEN 'EndGame' THEN 'Konec hry'
                        ELSE s."Stage"
                    END
                    || ': '
                    || CASE
                        WHEN s."InGameYear" IS NULL THEN ''
                        ELSE 'Rok ' || s."InGameYear"::text || ', '
                    END
                    || EXTRACT(DAY FROM (s."StartTime" AT TIME ZONE 'Europe/Prague'))::int::text
                    || '.'
                    || EXTRACT(MONTH FROM (s."StartTime" AT TIME ZONE 'Europe/Prague'))::int::text
                    || '. '
                    || to_char(s."StartTime" AT TIME ZONE 'Europe/Prague', 'HH24:MI')
                    || ' ('
                    || regexp_replace(
                        to_char(round((EXTRACT(EPOCH FROM s."Duration") / 3600.0)::numeric, 1), 'FM999999990.0'),
                        '\.0$',
                        '')
                    || ' h)'
                )
                FROM "GameTimeSlots" s
                WHERE q."TimeSlotId" = s."Id";
                """);

            migrationBuilder.DropColumn(
                name: "TimeSlotId",
                table: "Quests");
        }
    }
}
