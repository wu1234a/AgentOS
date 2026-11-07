using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQaReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BranchCoverage",
                table: "QaRuns",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LineCoverage",
                table: "QaRuns",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportIssues",
                table: "QaRuns",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportStatus",
                table: "QaRuns",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "QaTestCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QaRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    ReportPath = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SuiteName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ClassName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: true),
                    FailureType = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FailureDetail = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaTestCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaTestCases_QaRuns_QaRunId",
                        column: x => x.QaRunId,
                        principalTable: "QaRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QaTestCases_QaRunId_Ordinal",
                table: "QaTestCases",
                columns: new[] { "QaRunId", "Ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QaTestCases");

            migrationBuilder.DropColumn(
                name: "BranchCoverage",
                table: "QaRuns");

            migrationBuilder.DropColumn(
                name: "LineCoverage",
                table: "QaRuns");

            migrationBuilder.DropColumn(
                name: "ReportIssues",
                table: "QaRuns");

            migrationBuilder.DropColumn(
                name: "ReportStatus",
                table: "QaRuns");
        }
    }
}
