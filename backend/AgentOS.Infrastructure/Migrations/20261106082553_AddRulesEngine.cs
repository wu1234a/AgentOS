using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRulesEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModelProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ExecutionMode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ConfigurationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SecretReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    MaxCostPerTask = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelProfiles", x => x.Id);
                    table.CheckConstraint("CK_ModelProfiles_ConfigurationJson_IsJson", "ISJSON([ConfigurationJson]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "Skills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RelativePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Skills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Skills_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Agents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DefaultModelProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SystemPromptPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PermissionProfile = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Agents_ModelProfiles_DefaultModelProfileId",
                        column: x => x.DefaultModelProfileId,
                        principalTable: "ModelProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Agents_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AutomationRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourcePattern = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EventTypePattern = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    MatchJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SkillId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModelProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DispatchJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    MaxParallel = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    NotificationPolicy = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationRules", x => x.Id);
                    table.CheckConstraint("CK_AutomationRules_DispatchJson_IsJson", "ISJSON([DispatchJson]) = 1");
                    table.CheckConstraint("CK_AutomationRules_MatchJson_IsJson", "ISJSON([MatchJson]) = 1");
                    table.ForeignKey(
                        name: "FK_AutomationRules_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutomationRules_ModelProfiles_ModelProfileId",
                        column: x => x.ModelProfileId,
                        principalTable: "ModelProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutomationRules_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutomationRules_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RuleVersion = table.Column<int>(type: "int", nullable: true),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SkillId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModelProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ParentTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LogicalTaskKey = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    InputJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    NextRunAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                    table.CheckConstraint("CK_Tasks_InputJson_IsJson", "ISJSON([InputJson]) = 1");
                    table.ForeignKey(
                        name: "FK_Tasks_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tasks_AutomationRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "AutomationRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tasks_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tasks_ModelProfiles_ModelProfileId",
                        column: x => x.ModelProfileId,
                        principalTable: "ModelProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tasks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tasks_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Agents_DefaultModelProfileId",
                table: "Agents",
                column: "DefaultModelProfileId");

            migrationBuilder.CreateIndex(
                name: "UX_Agents_Global_Code",
                table: "Agents",
                column: "Code",
                unique: true,
                filter: "[ProjectId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Agents_Project_Code",
                table: "Agents",
                columns: new[] { "ProjectId", "Code" },
                unique: true,
                filter: "[ProjectId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRules_AgentId",
                table: "AutomationRules",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRules_IsEnabled",
                table: "AutomationRules",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRules_ModelProfileId",
                table: "AutomationRules",
                column: "ModelProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRules_ProjectId_IsEnabled",
                table: "AutomationRules",
                columns: new[] { "ProjectId", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRules_SkillId",
                table: "AutomationRules",
                column: "SkillId");

            migrationBuilder.CreateIndex(
                name: "UX_ModelProfiles_Code",
                table: "ModelProfiles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Skills_Project_Code_Version",
                table: "Skills",
                columns: new[] { "ProjectId", "Code", "Version" },
                unique: true,
                filter: "[ProjectId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_AgentId",
                table: "Tasks",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_EventId",
                table: "Tasks",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ModelProfileId",
                table: "Tasks",
                column: "ModelProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ProjectId",
                table: "Tasks",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_RuleId",
                table: "Tasks",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_SkillId",
                table: "Tasks",
                column: "SkillId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Status_Priority_NextRunAtUtc",
                table: "Tasks",
                columns: new[] { "Status", "Priority", "NextRunAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Tasks_LogicalTaskKey",
                table: "Tasks",
                column: "LogicalTaskKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Tasks_TaskCode",
                table: "Tasks",
                column: "TaskCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tasks");

            migrationBuilder.DropTable(
                name: "AutomationRules");

            migrationBuilder.DropTable(
                name: "Agents");

            migrationBuilder.DropTable(
                name: "Skills");

            migrationBuilder.DropTable(
                name: "ModelProfiles");
        }
    }
}
