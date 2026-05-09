using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddV1StandardsAndHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CaliperXapiCoverage",
                table: "LearningQualityReports",
                type: "decimal(6,4)",
                precision: 6,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CaseLikeCoverage",
                table: "LearningQualityReports",
                type: "decimal(6,4)",
                precision: 6,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "QtiLikeCoverage",
                table: "LearningQualityReports",
                type: "decimal(6,4)",
                precision: 6,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "StandardsAlignmentStatus",
                table: "LearningQualityReports",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "AudioByteLength",
                table: "ClassroomInteractions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "AudioExpiresAt",
                table: "ClassroomInteractions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AudioPurgedAt",
                table: "ClassroomInteractions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AudioByteLength",
                table: "AudioOverviewJobs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "AudioExpiresAt",
                table: "AudioOverviewJobs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AudioPurgedAt",
                table: "AudioOverviewJobs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StandardsExportRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConceptGraphSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExportType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    CaseCoverage = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    QtiCoverage = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    CaliperXapiCoverage = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandardsExportRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StandardsExportRuns_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StandardsValidationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConceptGraphSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CaseCoverage = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    QtiCoverage = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    CaliperXapiCoverage = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    CheckedItemCount = table.Column<int>(type: "int", nullable: false),
                    IssueCount = table.Column<int>(type: "int", nullable: false),
                    SummaryJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandardsValidationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StandardsValidationRuns_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StandardsExportItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StandardsExportRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StandardFamily = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EntityKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandardsExportItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StandardsExportItems_StandardsExportRuns_StandardsExportRunId",
                        column: x => x.StandardsExportRunId,
                        principalTable: "StandardsExportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StandardsValidationItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StandardsValidationRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StandardFamily = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EntityKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IssueCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UserSafeMessage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DetailJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandardsValidationItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StandardsValidationItems_StandardsValidationRuns_StandardsValidationRunId",
                        column: x => x.StandardsValidationRunId,
                        principalTable: "StandardsValidationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClassroomInteractions_AudioExpiresAt",
                table: "ClassroomInteractions",
                column: "AudioExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AudioOverviewJobs_AudioExpiresAt",
                table: "AudioOverviewJobs",
                column: "AudioExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_StandardsExportItems_StandardsExportRunId_StandardFamily_EntityType",
                table: "StandardsExportItems",
                columns: new[] { "StandardsExportRunId", "StandardFamily", "EntityType" });

            migrationBuilder.CreateIndex(
                name: "IX_StandardsExportRuns_UserId_TopicId_CreatedAt",
                table: "StandardsExportRuns",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StandardsValidationItems_StandardsValidationRunId_StandardFamily_Severity",
                table: "StandardsValidationItems",
                columns: new[] { "StandardsValidationRunId", "StandardFamily", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_StandardsValidationRuns_UserId_TopicId_CreatedAt",
                table: "StandardsValidationRuns",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StandardsExportItems");

            migrationBuilder.DropTable(
                name: "StandardsValidationItems");

            migrationBuilder.DropTable(
                name: "StandardsExportRuns");

            migrationBuilder.DropTable(
                name: "StandardsValidationRuns");

            migrationBuilder.DropIndex(
                name: "IX_ClassroomInteractions_AudioExpiresAt",
                table: "ClassroomInteractions");

            migrationBuilder.DropIndex(
                name: "IX_AudioOverviewJobs_AudioExpiresAt",
                table: "AudioOverviewJobs");

            migrationBuilder.DropColumn(
                name: "CaliperXapiCoverage",
                table: "LearningQualityReports");

            migrationBuilder.DropColumn(
                name: "CaseLikeCoverage",
                table: "LearningQualityReports");

            migrationBuilder.DropColumn(
                name: "QtiLikeCoverage",
                table: "LearningQualityReports");

            migrationBuilder.DropColumn(
                name: "StandardsAlignmentStatus",
                table: "LearningQualityReports");

            migrationBuilder.DropColumn(
                name: "AudioByteLength",
                table: "ClassroomInteractions");

            migrationBuilder.DropColumn(
                name: "AudioExpiresAt",
                table: "ClassroomInteractions");

            migrationBuilder.DropColumn(
                name: "AudioPurgedAt",
                table: "ClassroomInteractions");

            migrationBuilder.DropColumn(
                name: "AudioByteLength",
                table: "AudioOverviewJobs");

            migrationBuilder.DropColumn(
                name: "AudioExpiresAt",
                table: "AudioOverviewJobs");

            migrationBuilder.DropColumn(
                name: "AudioPurgedAt",
                table: "AudioOverviewJobs");
        }
    }
}
