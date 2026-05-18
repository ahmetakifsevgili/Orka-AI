using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLearningArtifactsEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LearningArtifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TutorTurnStateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TutorActionTraceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TeachingArtifactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActiveLessonSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StudentContextSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlanQualitySnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssessmentQualitySnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceEvidenceBundleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WikiNotebookSectionKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConceptKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConceptLabel = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ArtifactType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ArtifactStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RenderFormat = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SafeContent = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourceBasis = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CitationIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ToolTraceIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AccessibilityJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SafetyWarningsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningArtifacts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LearningArtifacts_UserId_ConceptKey_ArtifactStatus",
                table: "LearningArtifacts",
                columns: new[] { "UserId", "ConceptKey", "ArtifactStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningArtifacts_UserId_TeachingArtifactId",
                table: "LearningArtifacts",
                columns: new[] { "UserId", "TeachingArtifactId" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningArtifacts_UserId_TopicId_SessionId_CreatedAt",
                table: "LearningArtifacts",
                columns: new[] { "UserId", "TopicId", "SessionId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LearningArtifacts");
        }
    }
}
