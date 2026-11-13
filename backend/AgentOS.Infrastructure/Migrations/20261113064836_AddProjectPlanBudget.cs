using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectPlanBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ModelCalls",
                table: "ProjectPlans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PreviousFinalPlanJson",
                table: "ProjectPlans",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreviousRevision",
                table: "ProjectPlans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TokenBudget",
                table: "ProjectPlans",
                type: "int",
                nullable: false,
                defaultValue: 60000);

            migrationBuilder.AddColumn<int>(
                name: "TokensUsed",
                table: "ProjectPlans",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModelCalls",
                table: "ProjectPlans");

            migrationBuilder.DropColumn(
                name: "PreviousFinalPlanJson",
                table: "ProjectPlans");

            migrationBuilder.DropColumn(
                name: "PreviousRevision",
                table: "ProjectPlans");

            migrationBuilder.DropColumn(
                name: "TokenBudget",
                table: "ProjectPlans");

            migrationBuilder.DropColumn(
                name: "TokensUsed",
                table: "ProjectPlans");
        }
    }
}
