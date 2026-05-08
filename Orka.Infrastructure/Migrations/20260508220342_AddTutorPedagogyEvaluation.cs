using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTutorPedagogyEvaluation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CriticalPedagogyViolationCount",
                table: "LearningQualityReports",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TutorPedagogyScore",
                table: "LearningQualityReports",
                type: "decimal(6,4)",
                precision: 6,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TutorPedagogyStatus",
                table: "LearningQualityReports",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.CreateTable(
                name: "TutorGoldenScenarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DomainHint = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserMessage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpectedTeachingMode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpectedBehavior = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequiredRubricsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorGoldenScenarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TutorPedagogyEvaluationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TutorTurnStateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TutorActionTraceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TutorReflectionUpdateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OverallScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    HasCriticalViolation = table.Column<bool>(type: "bit", nullable: false),
                    WarningCount = table.Column<int>(type: "int", nullable: false),
                    CriticalViolationCount = table.Column<int>(type: "int", nullable: false),
                    LlmJudgeUsed = table.Column<bool>(type: "bit", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Recommendation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RunJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorPedagogyEvaluationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TutorPedagogyFeedbackPatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TutorPedagogyEvaluationRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PatchType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Feedback = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PatchJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorPedagogyFeedbackPatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TutorPedagogyEvaluationItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvaluationRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TutorTurnStateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TutorActionTraceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserMessage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AssistantAnswer = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TeachingMode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DirectAnswerPolicy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GroundingPolicy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActiveConceptKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ItemJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorPedagogyEvaluationItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TutorPedagogyEvaluationItems_TutorPedagogyEvaluationRuns_EvaluationRunId",
                        column: x => x.EvaluationRunId,
                        principalTable: "TutorPedagogyEvaluationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TutorPedagogyRubricScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvaluationRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TutorActionTraceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RubricKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Score = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsCritical = table.Column<bool>(type: "bit", nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Recommendation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorPedagogyRubricScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TutorPedagogyRubricScores_TutorPedagogyEvaluationRuns_EvaluationRunId",
                        column: x => x.EvaluationRunId,
                        principalTable: "TutorPedagogyEvaluationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TutorGoldenScenarios_ScenarioKey",
                table: "TutorGoldenScenarios",
                column: "ScenarioKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TutorPedagogyEvaluationItems_EvaluationRunId",
                table: "TutorPedagogyEvaluationItems",
                column: "EvaluationRunId");

            migrationBuilder.CreateIndex(
                name: "IX_TutorPedagogyEvaluationRuns_UserId_SessionId_CreatedAt",
                table: "TutorPedagogyEvaluationRuns",
                columns: new[] { "UserId", "SessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TutorPedagogyEvaluationRuns_UserId_TopicId_CreatedAt",
                table: "TutorPedagogyEvaluationRuns",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TutorPedagogyFeedbackPatches_UserId_TopicId_CreatedAt",
                table: "TutorPedagogyFeedbackPatches",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TutorPedagogyRubricScores_EvaluationRunId",
                table: "TutorPedagogyRubricScores",
                column: "EvaluationRunId");

            migrationBuilder.CreateIndex(
                name: "IX_TutorPedagogyRubricScores_UserId_TopicId_CreatedAt",
                table: "TutorPedagogyRubricScores",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TutorGoldenScenarios");

            migrationBuilder.DropTable(
                name: "TutorPedagogyEvaluationItems");

            migrationBuilder.DropTable(
                name: "TutorPedagogyFeedbackPatches");

            migrationBuilder.DropTable(
                name: "TutorPedagogyRubricScores");

            migrationBuilder.DropTable(
                name: "TutorPedagogyEvaluationRuns");

            migrationBuilder.DropColumn(
                name: "CriticalPedagogyViolationCount",
                table: "LearningQualityReports");

            migrationBuilder.DropColumn(
                name: "TutorPedagogyScore",
                table: "LearningQualityReports");

            migrationBuilder.DropColumn(
                name: "TutorPedagogyStatus",
                table: "LearningQualityReports");
        }
    }
}
