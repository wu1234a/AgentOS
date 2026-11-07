using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubtaskPhaseKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhaseKey",
                table: "Tasks",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ParentTaskId_PhaseKey",
                table: "Tasks",
                columns: new[] { "ParentTaskId", "PhaseKey" },
                unique: true,
                filter: "[PhaseKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_ParentTaskId_PhaseKey",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "PhaseKey",
                table: "Tasks");
        }
    }
}
