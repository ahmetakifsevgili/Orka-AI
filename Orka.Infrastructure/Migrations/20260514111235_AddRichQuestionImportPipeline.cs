using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRichQuestionImportPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImportFormat",
                table: "QuestionImportPreviews",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "structured_json");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedPackageJson",
                table: "QuestionImportPreviews",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackageTitle",
                table: "QuestionImportPreviews",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackageVersion",
                table: "QuestionImportPreviews",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImportFormat",
                table: "QuestionImportPreviews");

            migrationBuilder.DropColumn(
                name: "NormalizedPackageJson",
                table: "QuestionImportPreviews");

            migrationBuilder.DropColumn(
                name: "PackageTitle",
                table: "QuestionImportPreviews");

            migrationBuilder.DropColumn(
                name: "PackageVersion",
                table: "QuestionImportPreviews");
        }
    }
}
