using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSkillGlobalVersionIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_Skills_Global_Code_Version",
                table: "Skills",
                columns: new[] { "Code", "Version" },
                unique: true,
                filter: "[ProjectId] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Skills_Global_Code_Version",
                table: "Skills");
        }
    }
}
