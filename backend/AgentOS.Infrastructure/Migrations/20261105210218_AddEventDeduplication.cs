using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOS.Infrastructure.Migrations
{
    /// <summary>
    /// AOS-003 event deduplication: adds the payload fingerprint and the
    /// <c>Source + DedupeKey</c> unique index, after repairing legacy rows that would
    /// otherwise block the index.
    /// </summary>
    /// <remarks>
    /// Legacy rows are never deleted and their payloads are never rewritten:
    /// <list type="bullet">
    /// <item><description>Rows with an empty dedupe key get <c>legacy-{Id}</c>.</description></item>
    /// <item><description>For each <c>Source + DedupeKey</c> group the earliest row keeps the
    /// key; later rows are renamed to <c>{key}~legacy-dup-{Id}</c> so they stay queryable and
    /// auditable.</description></item>
    /// <item><description><c>PayloadHash</c> stays NULL for legacy rows and is backfilled
    /// lazily by the application the first time a duplicate hits that row.</description></item>
    /// </list>
    /// The rename is one-way: rolling this migration back does not restore the old keys.
    /// </remarks>
    public partial class AddEventDeduplication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayloadHash",
                table: "Events",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Events_PayloadHash_LowerHex64",
                table: "Events",
                sql: "[PayloadHash] IS NULL OR (LEN([PayloadHash]) = 64 AND [PayloadHash] COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9a-f]%')");

            // 1. Legacy rows that bypassed validation may carry an empty dedupe key.
            migrationBuilder.Sql("""
                UPDATE [Events]
                SET [DedupeKey] = CONCAT(N'legacy-', CONVERT(nvarchar(36), [Id]))
                WHERE [DedupeKey] IS NULL OR LTRIM(RTRIM([DedupeKey])) = N'';
                """);

            // 2. Keep every row, but move later duplicates out of the unique index's way.
            //    LEFT(400 - length('~legacy-dup-' + 36 chars guid)) keeps DedupeKey within its
            //    nvarchar(400) column.
            migrationBuilder.Sql("""
                ;WITH ranked AS (
                    SELECT [Id], [Source], [DedupeKey],
                           ROW_NUMBER() OVER (
                               PARTITION BY [Source], [DedupeKey]
                               ORDER BY [ReceivedAtUtc], [Id]) AS [rn]
                    FROM [Events]
                )
                UPDATE evt
                SET [DedupeKey] = CONCAT(LEFT(evt.[DedupeKey], 351), N'~legacy-dup-', CONVERT(nvarchar(36), evt.[Id]))
                FROM [Events] AS evt
                INNER JOIN ranked AS r ON r.[Id] = evt.[Id]
                WHERE r.[rn] > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "UX_Events_Source_DedupeKey",
                table: "Events",
                columns: new[] { "Source", "DedupeKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Events_Source_DedupeKey",
                table: "Events");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Events_PayloadHash_LowerHex64",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "PayloadHash",
                table: "Events");
        }
    }
}
