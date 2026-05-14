using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCentralExamPracticeResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CentralExamPracticeAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExamVariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VariantCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExamSectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SectionCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExamSubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubjectCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExamTopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TopicCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalQuestions = table.Column<int>(type: "int", nullable: false),
                    AnsweredCount = table.Column<int>(type: "int", nullable: false),
                    CorrectCount = table.Column<int>(type: "int", nullable: false),
                    WrongCount = table.Column<int>(type: "int", nullable: false),
                    BlankCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CentralExamPracticeAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CentralExamPracticeAttempts_ExamDefinitions_ExamDefinitionId",
                        column: x => x.ExamDefinitionId,
                        principalTable: "ExamDefinitions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CentralExamPracticeAttempts_ExamSections_ExamSectionId",
                        column: x => x.ExamSectionId,
                        principalTable: "ExamSections",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CentralExamPracticeAttempts_ExamSubjects_ExamSubjectId",
                        column: x => x.ExamSubjectId,
                        principalTable: "ExamSubjects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CentralExamPracticeAttempts_ExamTopics_ExamTopicId",
                        column: x => x.ExamTopicId,
                        principalTable: "ExamTopics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CentralExamPracticeAttempts_ExamVariants_ExamVariantId",
                        column: x => x.ExamVariantId,
                        principalTable: "ExamVariants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CentralExamPracticeAttempts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CentralExamPracticeAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PracticeAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamOutcomeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamTopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TopicCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    OutcomeCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    QuestionType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Difficulty = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SelectedOptionKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CorrectOptionKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    OptionKeysJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false),
                    IsBlank = table.Column<bool>(type: "bit", nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceTitle = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    SourceUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CentralExamPracticeAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CentralExamPracticeAnswers_CentralExamPracticeAttempts_PracticeAttemptId",
                        column: x => x.PracticeAttemptId,
                        principalTable: "CentralExamPracticeAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CentralExamPracticeAnswers_ExamOutcomes_ExamOutcomeId",
                        column: x => x.ExamOutcomeId,
                        principalTable: "ExamOutcomes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CentralExamPracticeAnswers_ExamTopics_ExamTopicId",
                        column: x => x.ExamTopicId,
                        principalTable: "ExamTopics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CentralExamPracticeAnswers_QuestionItems_QuestionItemId",
                        column: x => x.QuestionItemId,
                        principalTable: "QuestionItems",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamPracticeAnswers_ExamOutcomeId",
                table: "CentralExamPracticeAnswers",
                column: "ExamOutcomeId");

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamPracticeAnswers_ExamTopicId_ExamOutcomeId_IsCorrect_IsBlank",
                table: "CentralExamPracticeAnswers",
                columns: new[] { "ExamTopicId", "ExamOutcomeId", "IsCorrect", "IsBlank" });

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamPracticeAnswers_PracticeAttemptId_QuestionItemId",
                table: "CentralExamPracticeAnswers",
                columns: new[] { "PracticeAttemptId", "QuestionItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamPracticeAnswers_QuestionItemId",
                table: "CentralExamPracticeAnswers",
                column: "QuestionItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamPracticeAttempts_ExamDefinitionId",
                table: "CentralExamPracticeAttempts",
                column: "ExamDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamPracticeAttempts_ExamSectionId",
                table: "CentralExamPracticeAttempts",
                column: "ExamSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamPracticeAttempts_ExamSubjectId",
                table: "CentralExamPracticeAttempts",
                column: "ExamSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamPracticeAttempts_ExamTopicId",
                table: "CentralExamPracticeAttempts",
                column: "ExamTopicId");

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamPracticeAttempts_ExamVariantId",
                table: "CentralExamPracticeAttempts",
                column: "ExamVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamPracticeAttempts_UserId_ExamDefinitionId_ExamTopicId_StartedAt",
                table: "CentralExamPracticeAttempts",
                columns: new[] { "UserId", "ExamDefinitionId", "ExamTopicId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamPracticeAttempts_UserId_Status_StartedAt_IsDeleted",
                table: "CentralExamPracticeAttempts",
                columns: new[] { "UserId", "Status", "StartedAt", "IsDeleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CentralExamPracticeAnswers");

            migrationBuilder.DropTable(
                name: "CentralExamPracticeAttempts");
        }
    }
}
