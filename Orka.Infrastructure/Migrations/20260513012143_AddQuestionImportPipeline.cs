using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionImportPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuestionImportPreviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TotalCount = table.Column<int>(type: "int", nullable: false),
                    AcceptedCount = table.Column<int>(type: "int", nullable: false),
                    RejectedCount = table.Column<int>(type: "int", nullable: false),
                    WarningCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionImportPreviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionImportPreviews_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuestionImportPreviewItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionImportPreviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowIndex = table.Column<int>(type: "int", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IssuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NormalizedQuestionJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DuplicateQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionImportPreviewItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionImportPreviewItems_QuestionImportPreviews_QuestionImportPreviewId",
                        column: x => x.QuestionImportPreviewId,
                        principalTable: "QuestionImportPreviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionImportPreviewItems_QuestionImportPreviewId_ExternalId",
                table: "QuestionImportPreviewItems",
                columns: new[] { "QuestionImportPreviewId", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionImportPreviewItems_QuestionImportPreviewId_Status_IsDeleted",
                table: "QuestionImportPreviewItems",
                columns: new[] { "QuestionImportPreviewId", "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionImportPreviews_OwnerUserId_Status_ExpiresAt_IsDeleted",
                table: "QuestionImportPreviews",
                columns: new[] { "OwnerUserId", "Status", "ExpiresAt", "IsDeleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuestionImportPreviewItems");

            migrationBuilder.DropTable(
                name: "QuestionImportPreviews");
        }
    }
}
