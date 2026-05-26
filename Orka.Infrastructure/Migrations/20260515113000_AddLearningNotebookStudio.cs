using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Orka.Infrastructure.Data;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    [DbContext(typeof(OrkaDbContext))]
    [Migration("20260515113000_AddLearningNotebookStudio")]
    /// <inheritdoc />
    public partial class AddLearningNotebookStudio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LearningNotebookPacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WikiPageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WikiPageTitle = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    WikiPageKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ActiveLessonSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StudentContextSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceEvidenceBundleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WikiNotebookSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlanQualitySnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssessmentQualitySnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PackType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PackStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceReadiness = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EvidenceStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CompletedConceptKeysJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WeakConceptKeysJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MisconceptionKeysJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ArtifactIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NextActionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WarningsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SafeMetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningNotebookPacks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LearningNotebookPacks_UserId_PackType_PackStatus",
                table: "LearningNotebookPacks",
                columns: new[] { "UserId", "PackType", "PackStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningNotebookPacks_UserId_TopicId_SessionId_UpdatedAt",
                table: "LearningNotebookPacks",
                columns: new[] { "UserId", "TopicId", "SessionId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningNotebookPacks_UserId_WikiPageId_UpdatedAt",
                table: "LearningNotebookPacks",
                columns: new[] { "UserId", "WikiPageId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LearningNotebookPacks");
        }
    }
}
