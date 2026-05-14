using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionQualityAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuestionItemAnalyticsSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamVariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamSectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamSubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamTopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamOutcomeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    AnsweredCount = table.Column<int>(type: "int", nullable: false),
                    CorrectCount = table.Column<int>(type: "int", nullable: false),
                    WrongCount = table.Column<int>(type: "int", nullable: false),
                    BlankCount = table.Column<int>(type: "int", nullable: false),
                    CorrectnessRate = table.Column<decimal>(type: "decimal(8,4)", precision: 8, scale: 4, nullable: false),
                    BlankRate = table.Column<decimal>(type: "decimal(8,4)", precision: 8, scale: 4, nullable: false),
                    DifficultyEstimate = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DiscriminationStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    QualitySignal = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SampleSizeStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LastCalculatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionItemAnalyticsSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionItemAnalyticsSnapshots_ExamDefinitions_ExamDefinitionId",
                        column: x => x.ExamDefinitionId,
                        principalTable: "ExamDefinitions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionItemAnalyticsSnapshots_ExamOutcomes_ExamOutcomeId",
                        column: x => x.ExamOutcomeId,
                        principalTable: "ExamOutcomes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionItemAnalyticsSnapshots_ExamSections_ExamSectionId",
                        column: x => x.ExamSectionId,
                        principalTable: "ExamSections",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionItemAnalyticsSnapshots_ExamSubjects_ExamSubjectId",
                        column: x => x.ExamSubjectId,
                        principalTable: "ExamSubjects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionItemAnalyticsSnapshots_ExamTopics_ExamTopicId",
                        column: x => x.ExamTopicId,
                        principalTable: "ExamTopics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionItemAnalyticsSnapshots_ExamVariants_ExamVariantId",
                        column: x => x.ExamVariantId,
                        principalTable: "ExamVariants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionItemAnalyticsSnapshots_QuestionItems_QuestionItemId",
                        column: x => x.QuestionItemId,
                        principalTable: "QuestionItems",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuestionQualityReviewSignals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignalType = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionQualityReviewSignals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionQualityReviewSignals_QuestionItems_QuestionItemId",
                        column: x => x.QuestionItemId,
                        principalTable: "QuestionItems",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuestionOptionAnalyticsSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionItemAnalyticsSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OptionKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SelectionCount = table.Column<int>(type: "int", nullable: false),
                    CorrectSelectionCount = table.Column<int>(type: "int", nullable: false),
                    WrongSelectionCount = table.Column<int>(type: "int", nullable: false),
                    SelectionRate = table.Column<decimal>(type: "decimal(8,4)", precision: 8, scale: 4, nullable: false),
                    IsCorrectOption = table.Column<bool>(type: "bit", nullable: false),
                    DistractorSignal = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LastCalculatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionOptionAnalyticsSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionOptionAnalyticsSnapshots_QuestionItemAnalyticsSnapshots_QuestionItemAnalyticsSnapshotId",
                        column: x => x.QuestionItemAnalyticsSnapshotId,
                        principalTable: "QuestionItemAnalyticsSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuestionOptionAnalyticsSnapshots_QuestionItems_QuestionItemId",
                        column: x => x.QuestionItemId,
                        principalTable: "QuestionItems",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionItemAnalyticsSnapshots_ExamDefinitionId_ExamTopicId_ExamOutcomeId_IsDeleted",
                table: "QuestionItemAnalyticsSnapshots",
                columns: new[] { "ExamDefinitionId", "ExamTopicId", "ExamOutcomeId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionItemAnalyticsSnapshots_ExamOutcomeId",
                table: "QuestionItemAnalyticsSnapshots",
                column: "ExamOutcomeId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionItemAnalyticsSnapshots_ExamSectionId",
                table: "QuestionItemAnalyticsSnapshots",
                column: "ExamSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionItemAnalyticsSnapshots_ExamSubjectId",
                table: "QuestionItemAnalyticsSnapshots",
                column: "ExamSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionItemAnalyticsSnapshots_ExamTopicId",
                table: "QuestionItemAnalyticsSnapshots",
                column: "ExamTopicId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionItemAnalyticsSnapshots_ExamVariantId",
                table: "QuestionItemAnalyticsSnapshots",
                column: "ExamVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionItemAnalyticsSnapshots_QuestionItemId_LastCalculatedAt_IsDeleted",
                table: "QuestionItemAnalyticsSnapshots",
                columns: new[] { "QuestionItemId", "LastCalculatedAt", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionOptionAnalyticsSnapshots_QuestionItemAnalyticsSnapshotId",
                table: "QuestionOptionAnalyticsSnapshots",
                column: "QuestionItemAnalyticsSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionOptionAnalyticsSnapshots_QuestionItemId_OptionKey_IsDeleted",
                table: "QuestionOptionAnalyticsSnapshots",
                columns: new[] { "QuestionItemId", "OptionKey", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionQualityReviewSignals_QuestionItemId_SignalType_ResolvedAt_IsDeleted",
                table: "QuestionQualityReviewSignals",
                columns: new[] { "QuestionItemId", "SignalType", "ResolvedAt", "IsDeleted" });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuestionOptionAnalyticsSnapshots");

            migrationBuilder.DropTable(
                name: "QuestionQualityReviewSignals");

            migrationBuilder.DropTable(
                name: "QuestionItemAnalyticsSnapshots");

        }
    }
}
