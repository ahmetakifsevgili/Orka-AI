using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTutorExecutionEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Confidence",
                table: "TutorToolCalls",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "TutorToolCalls",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinishedAt",
                table: "TutorToolCalls",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "TutorToolCalls",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SafeMessage",
                table: "TutorToolCalls",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceCount",
                table: "TutorToolCalls",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartedAt",
                table: "TutorToolCalls",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Success",
                table: "TutorToolCalls",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ExternalUrl",
                table: "TeachingArtifacts",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "TeachingArtifacts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RenderError",
                table: "TeachingArtifacts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RenderedAt",
                table: "TeachingArtifacts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ConfidenceSelfRating",
                table: "QuizAttempts",
                type: "decimal(6,4)",
                precision: 6,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResponseTimeMs",
                table: "QuizAttempts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WasSkipped",
                table: "QuizAttempts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ArtifactRenderHealthStatus",
                table: "LearningQualityReports",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LearnerEvidenceStatus",
                table: "LearningQualityReports",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RagQualityStatus",
                table: "LearningQualityReports",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ToolExecutionHealthStatus",
                table: "LearningQualityReports",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "LastResponseTimeSeconds",
                table: "AssessmentItemStats",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SkipRate",
                table: "AssessmentItemStats",
                type: "decimal(6,4)",
                precision: 6,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalTimeSeconds",
                table: "AssessmentItemStats",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "RagEvaluationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConceptGraphSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QualityStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FaithfulnessScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    ContextRelevanceScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    AnswerRelevanceScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    CitationCoverageScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    ReportJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RagEvaluationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TutorMemoryFragments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TutorTurnStateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TutorActionTraceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FragmentType = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ConceptKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmbeddingJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Importance = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorMemoryFragments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RagEvaluationItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RagEvaluationRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Query = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Answer = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContextJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpectedCitationCount = table.Column<int>(type: "int", nullable: false),
                    CitationCount = table.Column<int>(type: "int", nullable: false),
                    FaithfulnessScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    ContextRelevanceScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    AnswerRelevanceScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    CitationCoverageScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RagEvaluationItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RagEvaluationItems_RagEvaluationRuns_RagEvaluationRunId",
                        column: x => x.RagEvaluationRunId,
                        principalTable: "RagEvaluationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RagEvaluationItems_RagEvaluationRunId",
                table: "RagEvaluationItems",
                column: "RagEvaluationRunId");

            migrationBuilder.CreateIndex(
                name: "IX_RagEvaluationRuns_UserId_TopicId_CreatedAt",
                table: "RagEvaluationRuns",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TutorMemoryFragments_UserId_FragmentType_CreatedAt",
                table: "TutorMemoryFragments",
                columns: new[] { "UserId", "FragmentType", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TutorMemoryFragments_UserId_TopicId_CreatedAt",
                table: "TutorMemoryFragments",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RagEvaluationItems");

            migrationBuilder.DropTable(
                name: "TutorMemoryFragments");

            migrationBuilder.DropTable(
                name: "RagEvaluationRuns");

            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "TutorToolCalls");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                table: "TutorToolCalls");

            migrationBuilder.DropColumn(
                name: "FinishedAt",
                table: "TutorToolCalls");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "TutorToolCalls");

            migrationBuilder.DropColumn(
                name: "SafeMessage",
                table: "TutorToolCalls");

            migrationBuilder.DropColumn(
                name: "SourceCount",
                table: "TutorToolCalls");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "TutorToolCalls");

            migrationBuilder.DropColumn(
                name: "Success",
                table: "TutorToolCalls");

            migrationBuilder.DropColumn(
                name: "ExternalUrl",
                table: "TeachingArtifacts");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "TeachingArtifacts");

            migrationBuilder.DropColumn(
                name: "RenderError",
                table: "TeachingArtifacts");

            migrationBuilder.DropColumn(
                name: "RenderedAt",
                table: "TeachingArtifacts");

            migrationBuilder.DropColumn(
                name: "ConfidenceSelfRating",
                table: "QuizAttempts");

            migrationBuilder.DropColumn(
                name: "ResponseTimeMs",
                table: "QuizAttempts");

            migrationBuilder.DropColumn(
                name: "WasSkipped",
                table: "QuizAttempts");

            migrationBuilder.DropColumn(
                name: "ArtifactRenderHealthStatus",
                table: "LearningQualityReports");

            migrationBuilder.DropColumn(
                name: "LearnerEvidenceStatus",
                table: "LearningQualityReports");

            migrationBuilder.DropColumn(
                name: "RagQualityStatus",
                table: "LearningQualityReports");

            migrationBuilder.DropColumn(
                name: "ToolExecutionHealthStatus",
                table: "LearningQualityReports");

            migrationBuilder.DropColumn(
                name: "LastResponseTimeSeconds",
                table: "AssessmentItemStats");

            migrationBuilder.DropColumn(
                name: "SkipRate",
                table: "AssessmentItemStats");

            migrationBuilder.DropColumn(
                name: "TotalTimeSeconds",
                table: "AssessmentItemStats");
        }
    }
}
