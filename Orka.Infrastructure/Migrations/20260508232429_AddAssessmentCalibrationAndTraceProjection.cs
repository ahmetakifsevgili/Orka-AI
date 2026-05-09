using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssessmentCalibrationAndTraceProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdaptiveReadiness",
                table: "LearningQualityReports",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<string>(
                name: "AssessmentCalibrationStatus",
                table: "LearningQualityReports",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<string>(
                name: "ItemBankHealth",
                table: "LearningQualityReports",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<string>(
                name: "TraceHealth",
                table: "LearningQualityReports",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<string>(
                name: "CalibrationStatus",
                table: "AssessmentItemStats",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "uncalibrated");

            migrationBuilder.AddColumn<decimal>(
                name: "DifficultyEstimate",
                table: "AssessmentItemStats",
                type: "decimal(6,4)",
                precision: 6,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscriminationEstimate",
                table: "AssessmentItemStats",
                type: "decimal(6,4)",
                precision: 6,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ExposureCount",
                table: "AssessmentItemStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSelectedAt",
                table: "AssessmentItemStats",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AdaptiveAssessmentSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuizRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConceptGraphSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TargetConceptsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StopReason = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MinItems = table.Column<int>(type: "int", nullable: false),
                    MaxItems = table.Column<int>(type: "int", nullable: false),
                    AnsweredCount = table.Column<int>(type: "int", nullable: false),
                    CorrectCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdaptiveAssessmentSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdaptiveAssessmentSessions_ConceptGraphSnapshots_ConceptGraphSnapshotId",
                        column: x => x.ConceptGraphSnapshotId,
                        principalTable: "ConceptGraphSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AdaptiveAssessmentSessions_QuizRuns_QuizRunId",
                        column: x => x.QuizRunId,
                        principalTable: "QuizRuns",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AdaptiveAssessmentSessions_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AdaptiveAssessmentSessions_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AdaptiveAssessmentSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AssessmentCalibrationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConceptGraphSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CalibrationStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AdaptiveReadiness = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ItemBankHealth = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    HealthyItemCount = table.Column<int>(type: "int", nullable: false),
                    ConceptCount = table.Column<int>(type: "int", nullable: false),
                    ReadyConceptCount = table.Column<int>(type: "int", nullable: false),
                    AverageDifficulty = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    AverageDiscrimination = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    AverageExposure = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    ReportJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentCalibrationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentCalibrationRuns_ConceptGraphSnapshots_ConceptGraphSnapshotId",
                        column: x => x.ConceptGraphSnapshotId,
                        principalTable: "ConceptGraphSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AssessmentCalibrationRuns_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AssessmentCalibrationRuns_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TutorTraceProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StreamKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StreamId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventGroup = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserSafeLabel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserSafeDetail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorTraceProjections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TutorTraceProjections_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TutorTraceProjections_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TutorTraceProjections_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AdaptiveAssessmentDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdaptiveAssessmentSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssessmentItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConceptKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SelectionScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    MasteryProbability = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    MasteryConfidence = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    ItemQualityScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    ExposurePenalty = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    DecisionReason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SelectedQuestionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WasAnswered = table.Column<bool>(type: "bit", nullable: false),
                    QuizAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdaptiveAssessmentDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdaptiveAssessmentDecisions_AdaptiveAssessmentSessions_AdaptiveAssessmentSessionId",
                        column: x => x.AdaptiveAssessmentSessionId,
                        principalTable: "AdaptiveAssessmentSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AdaptiveAssessmentDecisions_AssessmentItems_AssessmentItemId",
                        column: x => x.AssessmentItemId,
                        principalTable: "AssessmentItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AdaptiveAssessmentDecisions_QuizAttempts_QuizAttemptId",
                        column: x => x.QuizAttemptId,
                        principalTable: "QuizAttempts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AssessmentCalibrationItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssessmentCalibrationRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssessmentItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConceptKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    DifficultyEstimate = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    DiscriminationEstimate = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    ExposureCount = table.Column<int>(type: "int", nullable: false),
                    EvidenceCount = table.Column<int>(type: "int", nullable: false),
                    CalibrationStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentCalibrationItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentCalibrationItems_AssessmentCalibrationRuns_AssessmentCalibrationRunId",
                        column: x => x.AssessmentCalibrationRunId,
                        principalTable: "AssessmentCalibrationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentCalibrationItems_AssessmentItems_AssessmentItemId",
                        column: x => x.AssessmentItemId,
                        principalTable: "AssessmentItems",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdaptiveAssessmentDecisions_AdaptiveAssessmentSessionId_WasAnswered_CreatedAt",
                table: "AdaptiveAssessmentDecisions",
                columns: new[] { "AdaptiveAssessmentSessionId", "WasAnswered", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdaptiveAssessmentDecisions_AssessmentItemId",
                table: "AdaptiveAssessmentDecisions",
                column: "AssessmentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AdaptiveAssessmentDecisions_QuizAttemptId",
                table: "AdaptiveAssessmentDecisions",
                column: "QuizAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_AdaptiveAssessmentSessions_ConceptGraphSnapshotId",
                table: "AdaptiveAssessmentSessions",
                column: "ConceptGraphSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_AdaptiveAssessmentSessions_QuizRunId",
                table: "AdaptiveAssessmentSessions",
                column: "QuizRunId");

            migrationBuilder.CreateIndex(
                name: "IX_AdaptiveAssessmentSessions_SessionId",
                table: "AdaptiveAssessmentSessions",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AdaptiveAssessmentSessions_TopicId",
                table: "AdaptiveAssessmentSessions",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_AdaptiveAssessmentSessions_UserId_TopicId_CreatedAt",
                table: "AdaptiveAssessmentSessions",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentCalibrationItems_AssessmentCalibrationRunId",
                table: "AssessmentCalibrationItems",
                column: "AssessmentCalibrationRunId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentCalibrationItems_AssessmentItemId",
                table: "AssessmentCalibrationItems",
                column: "AssessmentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentCalibrationItems_UserId_TopicId_ConceptKey",
                table: "AssessmentCalibrationItems",
                columns: new[] { "UserId", "TopicId", "ConceptKey" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentCalibrationRuns_ConceptGraphSnapshotId",
                table: "AssessmentCalibrationRuns",
                column: "ConceptGraphSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentCalibrationRuns_TopicId",
                table: "AssessmentCalibrationRuns",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentCalibrationRuns_UserId_TopicId_CreatedAt",
                table: "AssessmentCalibrationRuns",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TutorTraceProjections_SessionId_StreamId",
                table: "TutorTraceProjections",
                columns: new[] { "SessionId", "StreamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TutorTraceProjections_TopicId",
                table: "TutorTraceProjections",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_TutorTraceProjections_UserId_SessionId_OccurredAt",
                table: "TutorTraceProjections",
                columns: new[] { "UserId", "SessionId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdaptiveAssessmentDecisions");

            migrationBuilder.DropTable(
                name: "AssessmentCalibrationItems");

            migrationBuilder.DropTable(
                name: "TutorTraceProjections");

            migrationBuilder.DropTable(
                name: "AdaptiveAssessmentSessions");

            migrationBuilder.DropTable(
                name: "AssessmentCalibrationRuns");

            migrationBuilder.DropColumn(
                name: "AdaptiveReadiness",
                table: "LearningQualityReports");

            migrationBuilder.DropColumn(
                name: "AssessmentCalibrationStatus",
                table: "LearningQualityReports");

            migrationBuilder.DropColumn(
                name: "ItemBankHealth",
                table: "LearningQualityReports");

            migrationBuilder.DropColumn(
                name: "TraceHealth",
                table: "LearningQualityReports");

            migrationBuilder.DropColumn(
                name: "CalibrationStatus",
                table: "AssessmentItemStats");

            migrationBuilder.DropColumn(
                name: "DifficultyEstimate",
                table: "AssessmentItemStats");

            migrationBuilder.DropColumn(
                name: "DiscriminationEstimate",
                table: "AssessmentItemStats");

            migrationBuilder.DropColumn(
                name: "ExposureCount",
                table: "AssessmentItemStats");

            migrationBuilder.DropColumn(
                name: "LastSelectedAt",
                table: "AssessmentItemStats");
        }
    }
}
