using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLearningSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActiveLessonSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlanRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuizRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConceptGraphSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceBundleHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SnapshotVersion = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActiveConceptKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ActiveConceptLabel = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ApprovedIntent = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ApprovedMainTopic = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ApprovedFocusArea = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ApprovedStudyGoal = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    GroundingMode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SourceEvidenceCount = table.Column<int>(type: "int", nullable: false),
                    WikiEvidenceCount = table.Column<int>(type: "int", nullable: false),
                    ToolEvidenceCount = table.Column<int>(type: "int", nullable: false),
                    RecentAttemptCount = table.Column<int>(type: "int", nullable: false),
                    WeakConceptCount = table.Column<int>(type: "int", nullable: false),
                    RemediationNeed = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LearnerState = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Confidence = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: true),
                    MasteryProbability = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: true),
                    EvidenceSummaryJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActiveLessonSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActiveLessonSnapshots_ConceptGraphSnapshots_ConceptGraphSnapshotId",
                        column: x => x.ConceptGraphSnapshotId,
                        principalTable: "ConceptGraphSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ActiveLessonSnapshots_QuizRuns_QuizRunId",
                        column: x => x.QuizRunId,
                        principalTable: "QuizRuns",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ActiveLessonSnapshots_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ActiveLessonSnapshots_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ActiveLessonSnapshots_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StudentContextSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SnapshotVersion = table.Column<int>(type: "int", nullable: false),
                    ConfidenceStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StrongConceptsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WeakConceptsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecentMisconceptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RemediationReadyJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReviewPressureJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceReadiness = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    GoalReadinessJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LearningMemoryJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentContextSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentContextSnapshots_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StudentContextSnapshots_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StudentContextSnapshots_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActiveLessonSnapshots_ConceptGraphSnapshotId",
                table: "ActiveLessonSnapshots",
                column: "ConceptGraphSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ActiveLessonSnapshots_QuizRunId",
                table: "ActiveLessonSnapshots",
                column: "QuizRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ActiveLessonSnapshots_SessionId",
                table: "ActiveLessonSnapshots",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ActiveLessonSnapshots_TopicId",
                table: "ActiveLessonSnapshots",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_ActiveLessonSnapshots_UserId_SessionId_Status_UpdatedAt",
                table: "ActiveLessonSnapshots",
                columns: new[] { "UserId", "SessionId", "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActiveLessonSnapshots_UserId_TopicId_Status_UpdatedAt",
                table: "ActiveLessonSnapshots",
                columns: new[] { "UserId", "TopicId", "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentContextSnapshots_SessionId",
                table: "StudentContextSnapshots",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentContextSnapshots_TopicId",
                table: "StudentContextSnapshots",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentContextSnapshots_UserId_SessionId_UpdatedAt",
                table: "StudentContextSnapshots",
                columns: new[] { "UserId", "SessionId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentContextSnapshots_UserId_TopicId_UpdatedAt",
                table: "StudentContextSnapshots",
                columns: new[] { "UserId", "TopicId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActiveLessonSnapshots");

            migrationBuilder.DropTable(
                name: "StudentContextSnapshots");
        }
    }
}
