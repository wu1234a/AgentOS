using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectPlanRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DecidedBy",
                table: "ProjectPlans",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReleaseApprovedBy",
                table: "ProjectPlans",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectPlanRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    FinalPlanJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Feedback = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    ModelProfileCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    TokensUsed = table.Column<int>(type: "int", nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectPlanRevisions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPlanRevisions_PlanId_Revision",
                table: "ProjectPlanRevisions",
                columns: new[] { "PlanId", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectPlanRevisions");

            migrationBuilder.DropColumn(
                name: "DecidedBy",
                table: "ProjectPlans");

            migrationBuilder.DropColumn(
                name: "ReleaseApprovedBy",
                table: "ProjectPlans");
        }
    }
}
