using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssessmentQualityMisconceptionContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssessmentMode",
                table: "AdaptiveAssessmentDecisions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "retrieval_practice");

            migrationBuilder.CreateTable(
                name: "AssessmentQualitySnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuizRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssessmentDraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlanQualitySnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActiveLessonSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StudentContextSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QualityStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ConceptCoverageScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    MisconceptionTargetingScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    DistractorQualityScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    LeakageSafetyScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    RemediationAlignmentScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    BlockingIssuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WarningIssuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AssessmentContractJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentQualitySnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentQualitySnapshots_ActiveLessonSnapshots_ActiveLessonSnapshotId",
                        column: x => x.ActiveLessonSnapshotId,
                        principalTable: "ActiveLessonSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AssessmentQualitySnapshots_LearningPlanQualitySnapshots_PlanQualitySnapshotId",
                        column: x => x.PlanQualitySnapshotId,
                        principalTable: "LearningPlanQualitySnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AssessmentQualitySnapshots_QuizRuns_QuizRunId",
                        column: x => x.QuizRunId,
                        principalTable: "QuizRuns",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AssessmentQualitySnapshots_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AssessmentQualitySnapshots_StudentContextSnapshots_StudentContextSnapshotId",
                        column: x => x.StudentContextSnapshotId,
                        principalTable: "StudentContextSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AssessmentQualitySnapshots_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AssessmentQualitySnapshots_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentQualitySnapshots_ActiveLessonSnapshotId",
                table: "AssessmentQualitySnapshots",
                column: "ActiveLessonSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentQualitySnapshots_PlanQualitySnapshotId",
                table: "AssessmentQualitySnapshots",
                column: "PlanQualitySnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentQualitySnapshots_QuizRunId",
                table: "AssessmentQualitySnapshots",
                column: "QuizRunId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentQualitySnapshots_SessionId",
                table: "AssessmentQualitySnapshots",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentQualitySnapshots_StudentContextSnapshotId",
                table: "AssessmentQualitySnapshots",
                column: "StudentContextSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentQualitySnapshots_TopicId",
                table: "AssessmentQualitySnapshots",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentQualitySnapshots_UserId_TopicId_SessionId_CreatedAt",
                table: "AssessmentQualitySnapshots",
                columns: new[] { "UserId", "TopicId", "SessionId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssessmentQualitySnapshots");

            migrationBuilder.DropColumn(
                name: "AssessmentMode",
                table: "AdaptiveAssessmentDecisions");
        }
    }
}
