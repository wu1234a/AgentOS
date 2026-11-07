using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityGates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QualityGateEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Repository = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HeadSha = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityGateEvaluations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QualityGateRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Repository = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HeadSha = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Conclusion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    RunAttempt = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityGateRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QualityGateEvaluations_Repository_HeadSha",
                table: "QualityGateEvaluations",
                columns: new[] { "Repository", "HeadSha" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QualityGateRuns_Repository_HeadSha_WorkflowName",
                table: "QualityGateRuns",
                columns: new[] { "Repository", "HeadSha", "WorkflowName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QualityGateEvaluations");

            migrationBuilder.DropTable(
                name: "QualityGateRuns");
        }
    }
}
