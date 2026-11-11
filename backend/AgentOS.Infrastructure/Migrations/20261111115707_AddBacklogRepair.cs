using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBacklogRepair : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RepairAttempt",
                table: "BacklogItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceItemId",
                table: "BacklogItems",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RepairAttempt",
                table: "BacklogItems");

            migrationBuilder.DropColumn(
                name: "SourceItemId",
                table: "BacklogItems");
        }
    }
}
