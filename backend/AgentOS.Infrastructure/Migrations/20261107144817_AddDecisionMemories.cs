using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDecisionMemories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DecisionMemories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceApprovalRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ValidFromUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    InvalidatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    InvalidatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    InvalidationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    HitCount = table.Column<int>(type: "int", nullable: false),
                    LastHitAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DecisionMemories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DecisionMemories_Decision_ExpiresAtUtc",
                table: "DecisionMemories",
                columns: new[] { "Decision", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DecisionMemories_ScopeType_ScopeKey_DecidedAtUtc",
                table: "DecisionMemories",
                columns: new[] { "ScopeType", "ScopeKey", "DecidedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DecisionMemories_SourceApprovalRequestId",
                table: "DecisionMemories",
                column: "SourceApprovalRequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DecisionMemories");
        }
    }
}
