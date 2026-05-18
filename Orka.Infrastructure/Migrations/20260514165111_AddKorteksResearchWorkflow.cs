using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKorteksResearchWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KorteksResearchWorkflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlanRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActiveLessonSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StudentContextSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Topic = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ApprovedIntent = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ApprovedMainTopic = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ApprovedFocusArea = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ApprovedStudyGoal = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    GroundingMode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceConfidence = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceCount = table.Column<int>(type: "int", nullable: false),
                    ToolCallCount = table.Column<int>(type: "int", nullable: false),
                    CanGroundTutorClaims = table.Column<bool>(type: "bit", nullable: false),
                    EvidenceSummaryJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SynthesisJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PlanContextJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    QuizContextJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TutorContextJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WikiContextJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SafetyIssuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PromptBlock = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KorteksResearchWorkflows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KorteksResearchWorkflows_ActiveLessonSnapshots_ActiveLessonSnapshotId",
                        column: x => x.ActiveLessonSnapshotId,
                        principalTable: "ActiveLessonSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_KorteksResearchWorkflows_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_KorteksResearchWorkflows_StudentContextSnapshots_StudentContextSnapshotId",
                        column: x => x.StudentContextSnapshotId,
                        principalTable: "StudentContextSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_KorteksResearchWorkflows_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_KorteksResearchWorkflows_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_KorteksResearchWorkflows_ActiveLessonSnapshotId",
                table: "KorteksResearchWorkflows",
                column: "ActiveLessonSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_KorteksResearchWorkflows_SessionId",
                table: "KorteksResearchWorkflows",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_KorteksResearchWorkflows_StudentContextSnapshotId",
                table: "KorteksResearchWorkflows",
                column: "StudentContextSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_KorteksResearchWorkflows_TopicId",
                table: "KorteksResearchWorkflows",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_KorteksResearchWorkflows_UserId_CreatedAt",
                table: "KorteksResearchWorkflows",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KorteksResearchWorkflows_UserId_SessionId_CreatedAt",
                table: "KorteksResearchWorkflows",
                columns: new[] { "UserId", "SessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KorteksResearchWorkflows_UserId_TopicId_CreatedAt",
                table: "KorteksResearchWorkflows",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KorteksResearchWorkflows");
        }
    }
}
