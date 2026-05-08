using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRealWorldTeachingEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EvidenceCitationCoverageStatus",
                table: "LearningQualityReports",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<string>(
                name: "EvidenceCoverageStatus",
                table: "LearningQualityReports",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<string>(
                name: "EvidenceFreshnessStatus",
                table: "LearningQualityReports",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<string>(
                name: "EvidenceProviderHealthStatus",
                table: "LearningQualityReports",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<string>(
                name: "ForumSignalUsageStatus",
                table: "LearningQualityReports",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "none");

            migrationBuilder.CreateTable(
                name: "TeachingEvidenceItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TutorTurnStateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TutorActionTraceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TutorToolCallId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EvidenceType = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ConceptKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Query = table.Column<string>(type: "nvarchar(900)", maxLength: 900, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(900)", maxLength: 900, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FactualClaim = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AnalogyCandidate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClassroomUse = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CitationUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    CitationLabel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Confidence = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    Freshness = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RiskLevel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RawPayloadHash = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RawPayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeachingEvidenceItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeachingEvidenceProviderHealth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EvidenceType = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    LastStatusCode = table.Column<int>(type: "int", nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CheckedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeachingEvidenceProviderHealth", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeachingEvidenceItems_EvidenceType_Provider_RawPayloadHash",
                table: "TeachingEvidenceItems",
                columns: new[] { "EvidenceType", "Provider", "RawPayloadHash" });

            migrationBuilder.CreateIndex(
                name: "IX_TeachingEvidenceItems_UserId_TopicId_CreatedAt",
                table: "TeachingEvidenceItems",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TeachingEvidenceItems_UserId_TutorActionTraceId_CreatedAt",
                table: "TeachingEvidenceItems",
                columns: new[] { "UserId", "TutorActionTraceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TeachingEvidenceProviderHealth_Provider_EvidenceType_CheckedAt",
                table: "TeachingEvidenceProviderHealth",
                columns: new[] { "Provider", "EvidenceType", "CheckedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeachingEvidenceItems");

            migrationBuilder.DropTable(
                name: "TeachingEvidenceProviderHealth");

            migrationBuilder.DropColumn(
                name: "EvidenceCitationCoverageStatus",
                table: "LearningQualityReports");

            migrationBuilder.DropColumn(
                name: "EvidenceCoverageStatus",
                table: "LearningQualityReports");

            migrationBuilder.DropColumn(
                name: "EvidenceFreshnessStatus",
                table: "LearningQualityReports");

            migrationBuilder.DropColumn(
                name: "EvidenceProviderHealthStatus",
                table: "LearningQualityReports");

            migrationBuilder.DropColumn(
                name: "ForumSignalUsageStatus",
                table: "LearningQualityReports");
        }
    }
}
