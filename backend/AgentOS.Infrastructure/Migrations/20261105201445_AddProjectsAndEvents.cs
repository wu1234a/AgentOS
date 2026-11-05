using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectsAndEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ExternalEventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DedupeKey = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ResourceUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubjectType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SubjectId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Priority = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.CheckConstraint("CK_Events_PayloadJson_IsJson", "ISJSON([PayloadJson]) = 1");
                    table.ForeignKey(
                        name: "FK_Events_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_ProjectId_EventType_ReceivedAtUtc",
                table: "Events",
                columns: new[] { "ProjectId", "EventType", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_ReceivedAtUtc",
                table: "Events",
                column: "ReceivedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Events_Source_ReceivedAtUtc",
                table: "Events",
                columns: new[] { "Source", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Slug",
                table: "Projects",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
