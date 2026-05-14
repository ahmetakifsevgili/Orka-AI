using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionBankCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuestionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamVariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamSectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamSubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamTopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamOutcomeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuestionType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Stem = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Difficulty = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CognitiveSkill = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    QualityStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LicenseStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceOrigin = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SourceTitle = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    SourceUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    Explanation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionItems_ExamDefinitions_ExamDefinitionId",
                        column: x => x.ExamDefinitionId,
                        principalTable: "ExamDefinitions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionItems_ExamOutcomes_ExamOutcomeId",
                        column: x => x.ExamOutcomeId,
                        principalTable: "ExamOutcomes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionItems_ExamSections_ExamSectionId",
                        column: x => x.ExamSectionId,
                        principalTable: "ExamSections",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionItems_ExamSubjects_ExamSubjectId",
                        column: x => x.ExamSubjectId,
                        principalTable: "ExamSubjects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionItems_ExamTopics_ExamTopicId",
                        column: x => x.ExamTopicId,
                        principalTable: "ExamTopics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionItems_ExamVariants_ExamVariantId",
                        column: x => x.ExamVariantId,
                        principalTable: "ExamVariants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionItems_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuestionExplanations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExplanationText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceTitle = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    SourceUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    Visibility = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsSafeForLearners = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionExplanations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionExplanations_QuestionItems_QuestionItemId",
                        column: x => x.QuestionItemId,
                        principalTable: "QuestionItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuestionOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OptionKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionOptions_QuestionItems_QuestionItemId",
                        column: x => x.QuestionItemId,
                        principalTable: "QuestionItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuestionOutcomeLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamOutcomeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    LinkStrength = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionOutcomeLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionOutcomeLinks_ExamOutcomes_ExamOutcomeId",
                        column: x => x.ExamOutcomeId,
                        principalTable: "ExamOutcomes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionOutcomeLinks_QuestionItems_QuestionItemId",
                        column: x => x.QuestionItemId,
                        principalTable: "QuestionItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuestionTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tag = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionTags_QuestionItems_QuestionItemId",
                        column: x => x.QuestionItemId,
                        principalTable: "QuestionItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionExplanations_QuestionItemId_Visibility_IsDeleted",
                table: "QuestionExplanations",
                columns: new[] { "QuestionItemId", "Visibility", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionItems_ExamDefinitionId_ExamVariantId_ExamSectionId_ExamSubjectId_ExamTopicId_ExamOutcomeId",
                table: "QuestionItems",
                columns: new[] { "ExamDefinitionId", "ExamVariantId", "ExamSectionId", "ExamSubjectId", "ExamTopicId", "ExamOutcomeId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionItems_ExamOutcomeId",
                table: "QuestionItems",
                column: "ExamOutcomeId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionItems_ExamSectionId",
                table: "QuestionItems",
                column: "ExamSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionItems_ExamSubjectId",
                table: "QuestionItems",
                column: "ExamSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionItems_ExamTopicId",
                table: "QuestionItems",
                column: "ExamTopicId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionItems_ExamVariantId",
                table: "QuestionItems",
                column: "ExamVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionItems_OwnerUserId_ExamDefinitionId_QualityStatus_IsDeleted",
                table: "QuestionItems",
                columns: new[] { "OwnerUserId", "ExamDefinitionId", "QualityStatus", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionItems_QuestionType_Difficulty_QualityStatus_IsDeleted",
                table: "QuestionItems",
                columns: new[] { "QuestionType", "Difficulty", "QualityStatus", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionOptions_QuestionItemId_OptionKey",
                table: "QuestionOptions",
                columns: new[] { "QuestionItemId", "OptionKey" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionOutcomeLinks_ExamOutcomeId_IsPrimary_IsDeleted",
                table: "QuestionOutcomeLinks",
                columns: new[] { "ExamOutcomeId", "IsPrimary", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionOutcomeLinks_QuestionItemId_ExamOutcomeId_IsDeleted",
                table: "QuestionOutcomeLinks",
                columns: new[] { "QuestionItemId", "ExamOutcomeId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionTags_QuestionItemId_Tag",
                table: "QuestionTags",
                columns: new[] { "QuestionItemId", "Tag" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuestionExplanations");

            migrationBuilder.DropTable(
                name: "QuestionOptions");

            migrationBuilder.DropTable(
                name: "QuestionOutcomeLinks");

            migrationBuilder.DropTable(
                name: "QuestionTags");

            migrationBuilder.DropTable(
                name: "QuestionItems");
        }
    }
}
