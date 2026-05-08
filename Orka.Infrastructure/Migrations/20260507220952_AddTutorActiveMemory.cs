using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTutorActiveMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AffectiveSignals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AffectiveState = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EvidenceType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Confidence = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffectiveSignals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CognitiveLoadSignals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CognitiveLoad = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EvidenceType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Confidence = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CognitiveLoadSignals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearnerProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PreferredStyleMode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StyleConfidence = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    AffectiveState = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CognitiveLoad = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EvidenceCount = table.Column<int>(type: "int", nullable: false),
                    ProfileJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearningStyleSignals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StyleMode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EvidenceType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    Confidence = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningStyleSignals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeachingArtifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TutorActionTraceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ArtifactType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RenderFormat = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeachingArtifacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TutorActionTraces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TutorTurnStateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TeachingMode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActiveConceptKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    StyleMode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DirectAnswerPolicy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GroundingPolicy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ToolPlanJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ArtifactPlanJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NextCheckPrompt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorActionTraces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TutorMemoryPatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PatchType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PatchJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorMemoryPatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TutorPolicyViolationsV2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TutorActionTraceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ViolationType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorPolicyViolationsV2", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TutorReflectionUpdates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TutorActionTraceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TutorTurnStateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PolicyApplied = table.Column<bool>(type: "bit", nullable: false),
                    SourceClaimWithoutSource = table.Column<bool>(type: "bit", nullable: false),
                    DirectAnswerRiskHandled = table.Column<bool>(type: "bit", nullable: false),
                    ArtifactRendered = table.Column<bool>(type: "bit", nullable: false),
                    MicroCheckAsked = table.Column<bool>(type: "bit", nullable: false),
                    ReflectionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorReflectionUpdates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TutorToolCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TutorActionTraceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ToolId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RiskLevel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FallbackReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorToolCalls", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TutorTurnStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkingMemorySnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConceptGraphSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserMessageHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActiveConceptKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TeachingMode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StyleMode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AffectiveState = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CognitiveLoad = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GroundingStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StateJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorTurnStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TutorWorkingMemorySnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkingMemoryVersion = table.Column<int>(type: "int", nullable: false),
                    ActiveConceptKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TeachingMode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StyleMode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AffectiveState = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CognitiveLoad = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsDegraded = table.Column<bool>(type: "bit", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorWorkingMemorySnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AffectiveSignals_UserId_TopicId_CreatedAt",
                table: "AffectiveSignals",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CognitiveLoadSignals_UserId_TopicId_CreatedAt",
                table: "CognitiveLoadSignals",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerProfiles_UserId_TopicId_UpdatedAt",
                table: "LearnerProfiles",
                columns: new[] { "UserId", "TopicId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningStyleSignals_UserId_TopicId_CreatedAt",
                table: "LearningStyleSignals",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TeachingArtifacts_UserId_TopicId_CreatedAt",
                table: "TeachingArtifacts",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TutorActionTraces_UserId_SessionId_CreatedAt",
                table: "TutorActionTraces",
                columns: new[] { "UserId", "SessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TutorActionTraces_UserId_TopicId_CreatedAt",
                table: "TutorActionTraces",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TutorMemoryPatches_UserId_TopicId_CreatedAt",
                table: "TutorMemoryPatches",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TutorPolicyViolationsV2_UserId_SessionId_CreatedAt",
                table: "TutorPolicyViolationsV2",
                columns: new[] { "UserId", "SessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TutorReflectionUpdates_UserId_SessionId_CreatedAt",
                table: "TutorReflectionUpdates",
                columns: new[] { "UserId", "SessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TutorToolCalls_UserId_SessionId_CreatedAt",
                table: "TutorToolCalls",
                columns: new[] { "UserId", "SessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TutorTurnStates_UserId_SessionId_CreatedAt",
                table: "TutorTurnStates",
                columns: new[] { "UserId", "SessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TutorTurnStates_UserId_TopicId_CreatedAt",
                table: "TutorTurnStates",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TutorWorkingMemorySnapshots_UserId_SessionId_CreatedAt",
                table: "TutorWorkingMemorySnapshots",
                columns: new[] { "UserId", "SessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TutorWorkingMemorySnapshots_UserId_TopicId_CreatedAt",
                table: "TutorWorkingMemorySnapshots",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AffectiveSignals");

            migrationBuilder.DropTable(
                name: "CognitiveLoadSignals");

            migrationBuilder.DropTable(
                name: "LearnerProfiles");

            migrationBuilder.DropTable(
                name: "LearningStyleSignals");

            migrationBuilder.DropTable(
                name: "TeachingArtifacts");

            migrationBuilder.DropTable(
                name: "TutorActionTraces");

            migrationBuilder.DropTable(
                name: "TutorMemoryPatches");

            migrationBuilder.DropTable(
                name: "TutorPolicyViolationsV2");

            migrationBuilder.DropTable(
                name: "TutorReflectionUpdates");

            migrationBuilder.DropTable(
                name: "TutorToolCalls");

            migrationBuilder.DropTable(
                name: "TutorTurnStates");

            migrationBuilder.DropTable(
                name: "TutorWorkingMemorySnapshots");
        }
    }
}
