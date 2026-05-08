using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLearningArchitectureGraph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssessmentItemId",
                table: "QuizAttempts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConceptGraphSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlanRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IntentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ApprovedResearchIntent = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TopicTitle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Domain = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceConfidence = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceBundleHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    GraphJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConceptGraphSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConceptGraphSnapshots_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ConceptGraphSnapshots_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ConceptMasteries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConceptGraphSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConceptKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MasteryScore = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Confidence = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    RemediationNeed = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PracticeReadiness = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MisconceptionEvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Correct = table.Column<int>(type: "int", nullable: false),
                    LastEvidenceAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConceptMasteries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConceptMasteries_ConceptGraphSnapshots_ConceptGraphSnapshotId",
                        column: x => x.ConceptGraphSnapshotId,
                        principalTable: "ConceptGraphSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ConceptMasteries_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ConceptMasteries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ConceptRelations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConceptGraphSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceConceptKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TargetConceptKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RelationType = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Weight = table.Column<double>(type: "float", nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConceptRelations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConceptRelations_ConceptGraphSnapshots_ConceptGraphSnapshotId",
                        column: x => x.ConceptGraphSnapshotId,
                        principalTable: "ConceptGraphSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DiagnosticProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuizRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlanRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConceptGraphSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AnsweredCount = table.Column<int>(type: "int", nullable: false),
                    CorrectCount = table.Column<int>(type: "int", nullable: false),
                    AccuracyPercent = table.Column<int>(type: "int", nullable: false),
                    MeasuredLevel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProfileJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosticProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiagnosticProfiles_ConceptGraphSnapshots_ConceptGraphSnapshotId",
                        column: x => x.ConceptGraphSnapshotId,
                        principalTable: "ConceptGraphSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DiagnosticProfiles_QuizRuns_QuizRunId",
                        column: x => x.QuizRunId,
                        principalTable: "QuizRuns",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DiagnosticProfiles_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DiagnosticProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LearningConcepts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConceptGraphSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StableKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DifficultyBand = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    PrerequisitesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MisconceptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LearningOutcomeKeysJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceEvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningConcepts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningConcepts_ConceptGraphSnapshots_ConceptGraphSnapshotId",
                        column: x => x.ConceptGraphSnapshotId,
                        principalTable: "ConceptGraphSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LearningOutcomes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConceptGraphSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StableKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StandardUri = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CognitiveLevel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningOutcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningOutcomes_ConceptGraphSnapshots_ConceptGraphSnapshotId",
                        column: x => x.ConceptGraphSnapshotId,
                        principalTable: "ConceptGraphSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssessmentItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuizRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlanRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConceptGraphSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LearningConceptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssessmentItemKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ConceptKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ConceptLabel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    QuestionType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CognitiveSkill = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Difficulty = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MisconceptionTarget = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EvidenceExpected = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OptionQualityRulesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScoringRuleJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LearningOutcomeKeysJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PromptSpecJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GeneratedQuestionJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Order = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentItems_ConceptGraphSnapshots_ConceptGraphSnapshotId",
                        column: x => x.ConceptGraphSnapshotId,
                        principalTable: "ConceptGraphSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AssessmentItems_LearningConcepts_LearningConceptId",
                        column: x => x.LearningConceptId,
                        principalTable: "LearningConcepts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AssessmentItems_QuizRuns_QuizRunId",
                        column: x => x.QuizRunId,
                        principalTable: "QuizRuns",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AssessmentItems_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AssessmentItems_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OutcomeAlignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConceptGraphSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LearningOutcomeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EntityType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EntityKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    AlignmentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Weight = table.Column<double>(type: "float", nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutcomeAlignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutcomeAlignments_ConceptGraphSnapshots_ConceptGraphSnapshotId",
                        column: x => x.ConceptGraphSnapshotId,
                        principalTable: "ConceptGraphSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OutcomeAlignments_LearningOutcomes_LearningOutcomeId",
                        column: x => x.LearningOutcomeId,
                        principalTable: "LearningOutcomes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LearningEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuizAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssessmentItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Verb = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ObjectType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ObjectId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConceptKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    SkillTag = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Score = table.Column<int>(type: "int", nullable: true),
                    IsPositive = table.Column<bool>(type: "bit", nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningEvents_AssessmentItems_AssessmentItemId",
                        column: x => x.AssessmentItemId,
                        principalTable: "AssessmentItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LearningEvents_QuizAttempts_QuizAttemptId",
                        column: x => x.QuizAttemptId,
                        principalTable: "QuizAttempts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LearningEvents_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LearningEvents_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LearningEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_AssessmentItemId",
                table: "QuizAttempts",
                column: "AssessmentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_UserId_AssessmentItemId",
                table: "QuizAttempts",
                columns: new[] { "UserId", "AssessmentItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentItems_ConceptGraphSnapshotId",
                table: "AssessmentItems",
                column: "ConceptGraphSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentItems_LearningConceptId",
                table: "AssessmentItems",
                column: "LearningConceptId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentItems_QuizRunId",
                table: "AssessmentItems",
                column: "QuizRunId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentItems_TopicId",
                table: "AssessmentItems",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentItems_UserId_PlanRequestId_Order",
                table: "AssessmentItems",
                columns: new[] { "UserId", "PlanRequestId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentItems_UserId_TopicId_ConceptKey",
                table: "AssessmentItems",
                columns: new[] { "UserId", "TopicId", "ConceptKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ConceptGraphSnapshots_PlanRequestId",
                table: "ConceptGraphSnapshots",
                column: "PlanRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ConceptGraphSnapshots_TopicId",
                table: "ConceptGraphSnapshots",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_ConceptGraphSnapshots_UserId_TopicId_IntentHash_CreatedAt",
                table: "ConceptGraphSnapshots",
                columns: new[] { "UserId", "TopicId", "IntentHash", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ConceptMasteries_ConceptGraphSnapshotId",
                table: "ConceptMasteries",
                column: "ConceptGraphSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ConceptMasteries_TopicId",
                table: "ConceptMasteries",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_ConceptMasteries_UserId_TopicId_ConceptKey",
                table: "ConceptMasteries",
                columns: new[] { "UserId", "TopicId", "ConceptKey" },
                unique: true,
                filter: "[TopicId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ConceptRelations_ConceptGraphSnapshotId_SourceConceptKey_TargetConceptKey_RelationType",
                table: "ConceptRelations",
                columns: new[] { "ConceptGraphSnapshotId", "SourceConceptKey", "TargetConceptKey", "RelationType" });

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticProfiles_ConceptGraphSnapshotId",
                table: "DiagnosticProfiles",
                column: "ConceptGraphSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticProfiles_QuizRunId",
                table: "DiagnosticProfiles",
                column: "QuizRunId");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticProfiles_TopicId",
                table: "DiagnosticProfiles",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticProfiles_UserId_PlanRequestId",
                table: "DiagnosticProfiles",
                columns: new[] { "UserId", "PlanRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticProfiles_UserId_TopicId_CreatedAt",
                table: "DiagnosticProfiles",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningConcepts_ConceptGraphSnapshotId_StableKey",
                table: "LearningConcepts",
                columns: new[] { "ConceptGraphSnapshotId", "StableKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearningEvents_AssessmentItemId",
                table: "LearningEvents",
                column: "AssessmentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningEvents_QuizAttemptId",
                table: "LearningEvents",
                column: "QuizAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningEvents_SessionId",
                table: "LearningEvents",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningEvents_TopicId",
                table: "LearningEvents",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningEvents_UserId_ConceptKey_OccurredAt",
                table: "LearningEvents",
                columns: new[] { "UserId", "ConceptKey", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningEvents_UserId_TopicId_EventType_OccurredAt",
                table: "LearningEvents",
                columns: new[] { "UserId", "TopicId", "EventType", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningOutcomes_ConceptGraphSnapshotId_StableKey",
                table: "LearningOutcomes",
                columns: new[] { "ConceptGraphSnapshotId", "StableKey" });

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeAlignments_ConceptGraphSnapshotId",
                table: "OutcomeAlignments",
                column: "ConceptGraphSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeAlignments_EntityType_EntityId",
                table: "OutcomeAlignments",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeAlignments_EntityType_EntityKey",
                table: "OutcomeAlignments",
                columns: new[] { "EntityType", "EntityKey" });

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeAlignments_LearningOutcomeId",
                table: "OutcomeAlignments",
                column: "LearningOutcomeId");

            migrationBuilder.AddForeignKey(
                name: "FK_QuizAttempts_AssessmentItems_AssessmentItemId",
                table: "QuizAttempts",
                column: "AssessmentItemId",
                principalTable: "AssessmentItems",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuizAttempts_AssessmentItems_AssessmentItemId",
                table: "QuizAttempts");

            migrationBuilder.DropTable(
                name: "ConceptMasteries");

            migrationBuilder.DropTable(
                name: "ConceptRelations");

            migrationBuilder.DropTable(
                name: "DiagnosticProfiles");

            migrationBuilder.DropTable(
                name: "LearningEvents");

            migrationBuilder.DropTable(
                name: "OutcomeAlignments");

            migrationBuilder.DropTable(
                name: "AssessmentItems");

            migrationBuilder.DropTable(
                name: "LearningOutcomes");

            migrationBuilder.DropTable(
                name: "LearningConcepts");

            migrationBuilder.DropTable(
                name: "ConceptGraphSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_QuizAttempts_AssessmentItemId",
                table: "QuizAttempts");

            migrationBuilder.DropIndex(
                name: "IX_QuizAttempts_UserId_AssessmentItemId",
                table: "QuizAttempts");

            migrationBuilder.DropColumn(
                name: "AssessmentItemId",
                table: "QuizAttempts");
        }
    }
}
