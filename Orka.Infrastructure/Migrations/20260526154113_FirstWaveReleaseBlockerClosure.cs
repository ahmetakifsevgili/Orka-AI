using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FirstWaveReleaseBlockerClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WikiPages_UserId",
                table: "WikiPages");

            migrationBuilder.DropIndex(
                name: "IX_WikiLinks_UserId",
                table: "WikiLinks");

            migrationBuilder.DropIndex(
                name: "IX_WikiBlocks_WikiPageId",
                table: "WikiBlocks");

            migrationBuilder.DropIndex(
                name: "IX_QuizAttempts_UserId_TopicId_QuestionHash",
                table: "QuizAttempts");

            migrationBuilder.AlterColumn<string>(
                name: "BlockType",
                table: "WikiBlocks",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_UserId_QuizRunId_AssessmentItemId",
                table: "QuizAttempts",
                columns: new[] { "UserId", "QuizRunId", "AssessmentItemId" },
                unique: true,
                filter: "[QuizRunId] IS NOT NULL AND [AssessmentItemId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_UserId_TopicId_QuestionHash",
                table: "QuizAttempts",
                columns: new[] { "UserId", "TopicId", "QuestionHash" },
                unique: true,
                filter: "[QuestionHash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QuizAttempts_UserId_QuizRunId_AssessmentItemId",
                table: "QuizAttempts");

            migrationBuilder.DropIndex(
                name: "IX_QuizAttempts_UserId_TopicId_QuestionHash",
                table: "QuizAttempts");

            migrationBuilder.AlterColumn<string>(
                name: "BlockType",
                table: "WikiBlocks",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.CreateIndex(
                name: "IX_WikiPages_UserId",
                table: "WikiPages",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WikiLinks_UserId",
                table: "WikiLinks",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WikiBlocks_WikiPageId",
                table: "WikiBlocks",
                column: "WikiPageId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_UserId_TopicId_QuestionHash",
                table: "QuizAttempts",
                columns: new[] { "UserId", "TopicId", "QuestionHash" });
        }
    }
}
