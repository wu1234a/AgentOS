using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TelegramNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChatId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    NextRetryAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MessageId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TelegramPollerState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NextOffset = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramPollerState", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TelegramProcessedUpdates",
                columns: table => new
                {
                    UpdateId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramProcessedUpdates", x => x.UpdateId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramNotifications_ApprovalId",
                table: "TelegramNotifications",
                column: "ApprovalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelegramNotifications_Status_NextRetryAtUtc",
                table: "TelegramNotifications",
                columns: new[] { "Status", "NextRetryAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TelegramNotifications");

            migrationBuilder.DropTable(
                name: "TelegramPollerState");

            migrationBuilder.DropTable(
                name: "TelegramProcessedUpdates");
        }
    }
}
