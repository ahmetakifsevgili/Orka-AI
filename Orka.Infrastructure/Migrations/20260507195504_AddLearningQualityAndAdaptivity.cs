using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLearningQualityAndAdaptivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssessmentItemStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssessmentItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConceptGraphSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConceptKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Correct = table.Column<int>(type: "int", nullable: false),
                    Incorrect = table.Column<int>(type: "int", nullable: false),
                    Skipped = table.Column<int>(type: "int", nullable: false),
                    CorrectRate = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    DiscriminationProxy = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    AverageTimeSeconds = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    QualityStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentItemStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentItemStats_AssessmentItems_AssessmentItemId",
                        column: x => x.AssessmentItemId,
                        principalTable: "AssessmentItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AssessmentItemStats_ConceptGraphSnapshots_ConceptGraphSnapshotId",
                        column: x => x.ConceptGraphSnapshotId,
                        principalTable: "ConceptGraphSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AssessmentItemStats_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AssessmentItemStats_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AssessmentQualityRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssessmentDraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlanRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuizRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConceptGraphSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QualityStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ConceptCoverage = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    LearningOutcomeCoverage = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    CognitiveSkillSpread = table.Column<int>(type: "int", nullable: false),
                    DifficultySpread = table.Column<int>(type: "int", nullable: false),
                    MisconceptionTargetingRatio = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    OptionQualityRatio = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    ScoringRulePresenceRatio = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    FailuresJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentQualityRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentQualityRuns_ConceptGraphSnapshots_ConceptGraphSnapshotId",
                        column: x => x.ConceptGraphSnapshotId,
                        principalTable: "ConceptGraphSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AssessmentQualityRuns_QuizRuns_QuizRunId",
                        column: x => x.QuizRunId,
                        principalTable: "QuizRuns",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AssessmentQualityRuns_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AssessmentQualityRuns_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ConceptGraphQualityRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConceptGraphSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QualityStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ConceptCount = table.Column<int>(type: "int", nullable: false),
                    DuplicateRatio = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    HasPrerequisiteCycle = table.Column<bool>(type: "bit", nullable: false),
                    OrphanConceptCount = table.Column<int>(type: "int", nullable: false),
                    OutcomeCoverage = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    MisconceptionCoverage = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    SourceEvidenceRatio = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    RelationDensity = table.Column<decimal>(type: "decimal(8,6)", precision: 8, scale: 6, nullable: false),
                    FailuresJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConceptGraphQualityRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConceptGraphQualityRuns_ConceptGraphSnapshots_ConceptGraphSnapshotId",
                        column: x => x.ConceptGraphSnapshotId,
                        principalTable: "ConceptGraphSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ConceptGraphQualityRuns_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ConceptGraphQualityRuns_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeTracingStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConceptGraphSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConceptKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PriorMastery = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    LearnRate = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    Slip = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    Guess = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    Decay = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    EvidenceCount = table.Column<int>(type: "int", nullable: false),
                    CorrectCount = table.Column<int>(type: "int", nullable: false),
                    IncorrectCount = table.Column<int>(type: "int", nullable: false),
                    MasteryProbability = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    Confidence = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    RemediationNeed = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PracticeReadiness = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastEvidenceAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeTracingStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeTracingStates_ConceptGraphSnapshots_ConceptGraphSnapshotId",
                        column: x => x.ConceptGraphSnapshotId,
                        principalTable: "ConceptGraphSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_KnowledgeTracingStates_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_KnowledgeTracingStates_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LearningEventSchemaViolations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LearningEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ViolationCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ViolationDetail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningEventSchemaViolations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningEventSchemaViolations_LearningEvents_LearningEventId",
                        column: x => x.LearningEventId,
                        principalTable: "LearningEvents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LearningEventSchemaViolations_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LearningEventSchemaViolations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LearningQualityReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConceptGraphSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlanRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QualityStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GraphQualityStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AssessmentQualityStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MasteryConfidenceStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TutorPolicyComplianceStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EventHealthStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceGroundingStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReportJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningQualityReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningQualityReports_ConceptGraphSnapshots_ConceptGraphSnapshotId",
                        column: x => x.ConceptGraphSnapshotId,
                        principalTable: "ConceptGraphSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LearningQualityReports_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LearningQualityReports_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ResourceConceptAlignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConceptGraphSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceTitle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceUri = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConceptKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OutcomeKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    AlignmentScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    EvidenceSnippet = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AlignmentStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceConceptAlignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourceConceptAlignments_ConceptGraphSnapshots_ConceptGraphSnapshotId",
                        column: x => x.ConceptGraphSnapshotId,
                        principalTable: "ConceptGraphSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ResourceConceptAlignments_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ResourceConceptAlignments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TutorPolicyTraces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConceptGraphSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActiveConceptKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    LearnerState = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RemediationNeed = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GroundingStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SelectedPedagogicalMove = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceEvidenceCount = table.Column<int>(type: "int", nullable: false),
                    DirectAnswerRisk = table.Column<bool>(type: "bit", nullable: false),
                    PolicyViolationsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InputHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorPolicyTraces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TutorPolicyTraces_ConceptGraphSnapshots_ConceptGraphSnapshotId",
                        column: x => x.ConceptGraphSnapshotId,
                        principalTable: "ConceptGraphSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TutorPolicyTraces_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TutorPolicyTraces_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TutorPolicyTraces_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentItemStats_AssessmentItemId",
                table: "AssessmentItemStats",
                column: "AssessmentItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentItemStats_ConceptGraphSnapshotId",
                table: "AssessmentItemStats",
                column: "ConceptGraphSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentItemStats_TopicId",
                table: "AssessmentItemStats",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentItemStats_UserId",
                table: "AssessmentItemStats",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentQualityRuns_AssessmentDraftId",
                table: "AssessmentQualityRuns",
                column: "AssessmentDraftId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentQualityRuns_ConceptGraphSnapshotId",
                table: "AssessmentQualityRuns",
                column: "ConceptGraphSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentQualityRuns_QuizRunId",
                table: "AssessmentQualityRuns",
                column: "QuizRunId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentQualityRuns_TopicId",
                table: "AssessmentQualityRuns",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentQualityRuns_UserId_TopicId_CreatedAt",
                table: "AssessmentQualityRuns",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ConceptGraphQualityRuns_ConceptGraphSnapshotId",
                table: "ConceptGraphQualityRuns",
                column: "ConceptGraphSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ConceptGraphQualityRuns_TopicId",
                table: "ConceptGraphQualityRuns",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_ConceptGraphQualityRuns_UserId_TopicId_CreatedAt",
                table: "ConceptGraphQualityRuns",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeTracingStates_ConceptGraphSnapshotId",
                table: "KnowledgeTracingStates",
                column: "ConceptGraphSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeTracingStates_TopicId",
                table: "KnowledgeTracingStates",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeTracingStates_UserId_TopicId_ConceptKey",
                table: "KnowledgeTracingStates",
                columns: new[] { "UserId", "TopicId", "ConceptKey" },
                unique: true,
                filter: "[TopicId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LearningEventSchemaViolations_LearningEventId",
                table: "LearningEventSchemaViolations",
                column: "LearningEventId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningEventSchemaViolations_TopicId",
                table: "LearningEventSchemaViolations",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningEventSchemaViolations_UserId_TopicId_CreatedAt",
                table: "LearningEventSchemaViolations",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningQualityReports_ConceptGraphSnapshotId",
                table: "LearningQualityReports",
                column: "ConceptGraphSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningQualityReports_TopicId",
                table: "LearningQualityReports",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningQualityReports_UserId_TopicId_GeneratedAt",
                table: "LearningQualityReports",
                columns: new[] { "UserId", "TopicId", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceConceptAlignments_ConceptGraphSnapshotId_ConceptKey",
                table: "ResourceConceptAlignments",
                columns: new[] { "ConceptGraphSnapshotId", "ConceptKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceConceptAlignments_TopicId",
                table: "ResourceConceptAlignments",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceConceptAlignments_UserId_TopicId_CreatedAt",
                table: "ResourceConceptAlignments",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TutorPolicyTraces_ConceptGraphSnapshotId",
                table: "TutorPolicyTraces",
                column: "ConceptGraphSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_TutorPolicyTraces_SessionId",
                table: "TutorPolicyTraces",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_TutorPolicyTraces_TopicId",
                table: "TutorPolicyTraces",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_TutorPolicyTraces_UserId_SessionId_CreatedAt",
                table: "TutorPolicyTraces",
                columns: new[] { "UserId", "SessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TutorPolicyTraces_UserId_TopicId_CreatedAt",
                table: "TutorPolicyTraces",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssessmentItemStats");

            migrationBuilder.DropTable(
                name: "AssessmentQualityRuns");

            migrationBuilder.DropTable(
                name: "ConceptGraphQualityRuns");

            migrationBuilder.DropTable(
                name: "KnowledgeTracingStates");

            migrationBuilder.DropTable(
                name: "LearningEventSchemaViolations");

            migrationBuilder.DropTable(
                name: "LearningQualityReports");

            migrationBuilder.DropTable(
                name: "ResourceConceptAlignments");

            migrationBuilder.DropTable(
                name: "TutorPolicyTraces");
        }
    }
}
