using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCentralExamMiniDeneme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CentralExamDenemeBlueprints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamVariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Visibility = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    VerificationStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OfficialClaimAllowed = table.Column<bool>(type: "bit", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: true),
                    TotalQuestionCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CentralExamDenemeBlueprints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CentralExamDenemeBlueprints_ExamDefinitions_ExamDefinitionId",
                        column: x => x.ExamDefinitionId,
                        principalTable: "ExamDefinitions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CentralExamDenemeBlueprints_ExamVariants_ExamVariantId",
                        column: x => x.ExamVariantId,
                        principalTable: "ExamVariants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CentralExamDenemeBlueprints_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CentralExamDenemeAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlueprintId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExamVariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VariantCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DurationMinutes = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_CentralExamDenemeAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CentralExamDenemeAttempts_CentralExamDenemeBlueprints_BlueprintId",
                        column: x => x.BlueprintId,
                        principalTable: "CentralExamDenemeBlueprints",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CentralExamDenemeAttempts_ExamDefinitions_ExamDefinitionId",
                        column: x => x.ExamDefinitionId,
                        principalTable: "ExamDefinitions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CentralExamDenemeAttempts_ExamVariants_ExamVariantId",
                        column: x => x.ExamVariantId,
                        principalTable: "ExamVariants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CentralExamDenemeAttempts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CentralExamDenemeBlueprintSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlueprintId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamSectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamSubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamTopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SectionCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SubjectCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TopicCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    QuestionCount = table.Column<int>(type: "int", nullable: false),
                    DifficultyMixJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CentralExamDenemeBlueprintSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CentralExamDenemeBlueprintSections_CentralExamDenemeBlueprints_BlueprintId",
                        column: x => x.BlueprintId,
                        principalTable: "CentralExamDenemeBlueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CentralExamDenemeBlueprintSections_ExamSections_ExamSectionId",
                        column: x => x.ExamSectionId,
                        principalTable: "ExamSections",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CentralExamDenemeBlueprintSections_ExamSubjects_ExamSubjectId",
                        column: x => x.ExamSubjectId,
                        principalTable: "ExamSubjects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CentralExamDenemeBlueprintSections_ExamTopics_ExamTopicId",
                        column: x => x.ExamTopicId,
                        principalTable: "ExamTopics",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CentralExamDenemeAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DenemeAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamSectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamSubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamTopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamOutcomeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SectionCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SubjectCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
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
                    table.PrimaryKey("PK_CentralExamDenemeAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CentralExamDenemeAnswers_CentralExamDenemeAttempts_DenemeAttemptId",
                        column: x => x.DenemeAttemptId,
                        principalTable: "CentralExamDenemeAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CentralExamDenemeAnswers_ExamOutcomes_ExamOutcomeId",
                        column: x => x.ExamOutcomeId,
                        principalTable: "ExamOutcomes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CentralExamDenemeAnswers_ExamSections_ExamSectionId",
                        column: x => x.ExamSectionId,
                        principalTable: "ExamSections",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CentralExamDenemeAnswers_ExamSubjects_ExamSubjectId",
                        column: x => x.ExamSubjectId,
                        principalTable: "ExamSubjects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CentralExamDenemeAnswers_ExamTopics_ExamTopicId",
                        column: x => x.ExamTopicId,
                        principalTable: "ExamTopics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CentralExamDenemeAnswers_QuestionItems_QuestionItemId",
                        column: x => x.QuestionItemId,
                        principalTable: "QuestionItems",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamDenemeAnswers_DenemeAttemptId_QuestionItemId",
                table: "CentralExamDenemeAnswers",
                columns: new[] { "DenemeAttemptId", "QuestionItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamDenemeAnswers_ExamOutcomeId",
                table: "CentralExamDenemeAnswers",
                column: "ExamOutcomeId");

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamDenemeAnswers_ExamSectionId_ExamSubjectId_ExamTopicId_ExamOutcomeId",
                table: "CentralExamDenemeAnswers",
                columns: new[] { "ExamSectionId", "ExamSubjectId", "ExamTopicId", "ExamOutcomeId" });

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamDenemeAnswers_ExamSubjectId",
                table: "CentralExamDenemeAnswers",
                column: "ExamSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamDenemeAnswers_ExamTopicId",
                table: "CentralExamDenemeAnswers",
                column: "ExamTopicId");

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamDenemeAnswers_QuestionItemId",
                table: "CentralExamDenemeAnswers",
                column: "QuestionItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamDenemeAttempts_BlueprintId",
                table: "CentralExamDenemeAttempts",
                column: "BlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamDenemeAttempts_ExamDefinitionId",
                table: "CentralExamDenemeAttempts",
                column: "ExamDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamDenemeAttempts_ExamVariantId",
                table: "CentralExamDenemeAttempts",
                column: "ExamVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamDenemeAttempts_UserId_BlueprintId_StartedAt",
                table: "CentralExamDenemeAttempts",
                columns: new[] { "UserId", "BlueprintId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamDenemeAttempts_UserId_Status_StartedAt_IsDeleted",
                table: "CentralExamDenemeAttempts",
                columns: new[] { "UserId", "Status", "StartedAt", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamDenemeBlueprints_Code_OwnerUserId_IsDeleted",
                table: "CentralExamDenemeBlueprints",
                columns: new[] { "Code", "OwnerUserId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamDenemeBlueprints_ExamDefinitionId",
                table: "CentralExamDenemeBlueprints",
                column: "ExamDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamDenemeBlueprints_ExamVariantId",
                table: "CentralExamDenemeBlueprints",
                column: "ExamVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamDenemeBlueprints_OwnerUserId",
                table: "CentralExamDenemeBlueprints",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamDenemeBlueprintSections_BlueprintId_SortOrder_IsDeleted",
                table: "CentralExamDenemeBlueprintSections",
                columns: new[] { "BlueprintId", "SortOrder", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamDenemeBlueprintSections_ExamSectionId",
                table: "CentralExamDenemeBlueprintSections",
                column: "ExamSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamDenemeBlueprintSections_ExamSubjectId",
                table: "CentralExamDenemeBlueprintSections",
                column: "ExamSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_CentralExamDenemeBlueprintSections_ExamTopicId",
                table: "CentralExamDenemeBlueprintSections",
                column: "ExamTopicId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CentralExamDenemeAnswers");

            migrationBuilder.DropTable(
                name: "CentralExamDenemeBlueprintSections");

            migrationBuilder.DropTable(
                name: "CentralExamDenemeAttempts");

            migrationBuilder.DropTable(
                name: "CentralExamDenemeBlueprints");
        }
    }
}
