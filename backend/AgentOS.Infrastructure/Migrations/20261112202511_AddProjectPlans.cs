using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EpicKey",
                table: "BacklogItems",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlanId",
                table: "BacklogItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectPlanContributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModelProfileCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectPlanContributions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Goal = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    Constraints = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    DeliveryJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModelProfileCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProposalsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CritiquesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FinalPlanJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectPlans", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPlanContributions_PlanId_Round_Role",
                table: "ProjectPlanContributions",
                columns: new[] { "PlanId", "Round", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPlans_ProjectId_Status_CreatedAtUtc",
                table: "ProjectPlans",
                columns: new[] { "ProjectId", "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectPlanContributions");

            migrationBuilder.DropTable(
                name: "ProjectPlans");

            migrationBuilder.DropColumn(
                name: "EpicKey",
                table: "BacklogItems");

            migrationBuilder.DropColumn(
                name: "PlanId",
                table: "BacklogItems");
        }
    }
}
