using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProfessionalQuestionBankBindingV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiagnosticSignalJson",
                table: "QuestionOptions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MisconceptionKey",
                table: "QuestionOptions",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Rationale",
                table: "QuestionOptions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssessmentItemId",
                table: "QuestionItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalibrationStatus",
                table: "QuestionItems",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConceptGraphSnapshotId",
                table: "QuestionItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConceptKey",
                table: "QuestionItems",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConceptLabel",
                table: "QuestionItems",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceExpected",
                table: "QuestionItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LearningConceptId",
                table: "QuestionItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LearningTopicId",
                table: "QuestionItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MisconceptionTarget",
                table: "QuestionItems",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlanRequestId",
                table: "QuestionItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuestionBankSource",
                table: "QuestionItems",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "curated_question_item");

            migrationBuilder.AddColumn<Guid>(
                name: "QuizRunId",
                table: "QuestionItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScoringRuleJson",
                table: "QuestionItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisualReadinessStatus",
                table: "QuestionItems",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "not_required");

            migrationBuilder.AddColumn<string>(
                name: "GenerationModel",
                table: "QuestionAssets",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GenerationPromptHash",
                table: "QuestionAssets",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GenerationProvider",
                table: "QuestionAssets",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RenderStrategy",
                table: "QuestionAssets",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidationReportJson",
                table: "QuestionAssets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisualReadinessStatus",
                table: "QuestionAssets",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "needs_validation");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionItems_AssessmentItemId_IsDeleted",
                table: "QuestionItems",
                columns: new[] { "AssessmentItemId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionItems_ConceptGraphSnapshotId",
                table: "QuestionItems",
                column: "ConceptGraphSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionItems_LearningConceptId",
                table: "QuestionItems",
                column: "LearningConceptId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionItems_LearningTopicId",
                table: "QuestionItems",
                column: "LearningTopicId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionItems_OwnerUserId_LearningTopicId_ConceptKey_QualityStatus_IsDeleted",
                table: "QuestionItems",
                columns: new[] { "OwnerUserId", "LearningTopicId", "ConceptKey", "QualityStatus", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionItems_PlanRequestId_QuizRunId_QuestionBankSource",
                table: "QuestionItems",
                columns: new[] { "PlanRequestId", "QuizRunId", "QuestionBankSource" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionItems_QuizRunId",
                table: "QuestionItems",
                column: "QuizRunId");

            migrationBuilder.AddForeignKey(
                name: "FK_QuestionItems_AssessmentItems_AssessmentItemId",
                table: "QuestionItems",
                column: "AssessmentItemId",
                principalTable: "AssessmentItems",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_QuestionItems_ConceptGraphSnapshots_ConceptGraphSnapshotId",
                table: "QuestionItems",
                column: "ConceptGraphSnapshotId",
                principalTable: "ConceptGraphSnapshots",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_QuestionItems_LearningConcepts_LearningConceptId",
                table: "QuestionItems",
                column: "LearningConceptId",
                principalTable: "LearningConcepts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_QuestionItems_QuizRuns_QuizRunId",
                table: "QuestionItems",
                column: "QuizRunId",
                principalTable: "QuizRuns",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_QuestionItems_Topics_LearningTopicId",
                table: "QuestionItems",
                column: "LearningTopicId",
                principalTable: "Topics",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuestionItems_AssessmentItems_AssessmentItemId",
                table: "QuestionItems");

            migrationBuilder.DropForeignKey(
                name: "FK_QuestionItems_ConceptGraphSnapshots_ConceptGraphSnapshotId",
                table: "QuestionItems");

            migrationBuilder.DropForeignKey(
                name: "FK_QuestionItems_LearningConcepts_LearningConceptId",
                table: "QuestionItems");

            migrationBuilder.DropForeignKey(
                name: "FK_QuestionItems_QuizRuns_QuizRunId",
                table: "QuestionItems");

            migrationBuilder.DropForeignKey(
                name: "FK_QuestionItems_Topics_LearningTopicId",
                table: "QuestionItems");

            migrationBuilder.DropIndex(
                name: "IX_QuestionItems_AssessmentItemId_IsDeleted",
                table: "QuestionItems");

            migrationBuilder.DropIndex(
                name: "IX_QuestionItems_ConceptGraphSnapshotId",
                table: "QuestionItems");

            migrationBuilder.DropIndex(
                name: "IX_QuestionItems_LearningConceptId",
                table: "QuestionItems");

            migrationBuilder.DropIndex(
                name: "IX_QuestionItems_LearningTopicId",
                table: "QuestionItems");

            migrationBuilder.DropIndex(
                name: "IX_QuestionItems_OwnerUserId_LearningTopicId_ConceptKey_QualityStatus_IsDeleted",
                table: "QuestionItems");

            migrationBuilder.DropIndex(
                name: "IX_QuestionItems_PlanRequestId_QuizRunId_QuestionBankSource",
                table: "QuestionItems");

            migrationBuilder.DropIndex(
                name: "IX_QuestionItems_QuizRunId",
                table: "QuestionItems");

            migrationBuilder.DropColumn(
                name: "DiagnosticSignalJson",
                table: "QuestionOptions");

            migrationBuilder.DropColumn(
                name: "MisconceptionKey",
                table: "QuestionOptions");

            migrationBuilder.DropColumn(
                name: "Rationale",
                table: "QuestionOptions");

            migrationBuilder.DropColumn(
                name: "AssessmentItemId",
                table: "QuestionItems");

            migrationBuilder.DropColumn(
                name: "CalibrationStatus",
                table: "QuestionItems");

            migrationBuilder.DropColumn(
                name: "ConceptGraphSnapshotId",
                table: "QuestionItems");

            migrationBuilder.DropColumn(
                name: "ConceptKey",
                table: "QuestionItems");

            migrationBuilder.DropColumn(
                name: "ConceptLabel",
                table: "QuestionItems");

            migrationBuilder.DropColumn(
                name: "EvidenceExpected",
                table: "QuestionItems");

            migrationBuilder.DropColumn(
                name: "LearningConceptId",
                table: "QuestionItems");

            migrationBuilder.DropColumn(
                name: "LearningTopicId",
                table: "QuestionItems");

            migrationBuilder.DropColumn(
                name: "MisconceptionTarget",
                table: "QuestionItems");

            migrationBuilder.DropColumn(
                name: "PlanRequestId",
                table: "QuestionItems");

            migrationBuilder.DropColumn(
                name: "QuestionBankSource",
                table: "QuestionItems");

            migrationBuilder.DropColumn(
                name: "QuizRunId",
                table: "QuestionItems");

            migrationBuilder.DropColumn(
                name: "ScoringRuleJson",
                table: "QuestionItems");

            migrationBuilder.DropColumn(
                name: "VisualReadinessStatus",
                table: "QuestionItems");

            migrationBuilder.DropColumn(
                name: "GenerationModel",
                table: "QuestionAssets");

            migrationBuilder.DropColumn(
                name: "GenerationPromptHash",
                table: "QuestionAssets");

            migrationBuilder.DropColumn(
                name: "GenerationProvider",
                table: "QuestionAssets");

            migrationBuilder.DropColumn(
                name: "RenderStrategy",
                table: "QuestionAssets");

            migrationBuilder.DropColumn(
                name: "ValidationReportJson",
                table: "QuestionAssets");

            migrationBuilder.DropColumn(
                name: "VisualReadinessStatus",
                table: "QuestionAssets");
        }
    }
}
