using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiTenancyAndTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "WikiPages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Topics",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Sessions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "QuizRuns",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "QuizAttempts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Messages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "AssessmentItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            BackfillTenantIds(migrationBuilder, "WikiPages");
            BackfillTenantIds(migrationBuilder, "Topics");
            BackfillTenantIds(migrationBuilder, "Sessions");
            BackfillTenantIds(migrationBuilder, "QuizRuns");
            BackfillTenantIds(migrationBuilder, "QuizAttempts");
            BackfillTenantIds(migrationBuilder, "Messages");
            BackfillTenantIds(migrationBuilder, "AssessmentItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "WikiPages");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "QuizRuns");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "QuizAttempts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AssessmentItems");
        }

        private static void BackfillTenantIds(MigrationBuilder migrationBuilder, string table)
        {
            migrationBuilder.Sql($"""
                UPDATE [{table}]
                SET [TenantId] = CONCAT(N'user:', CONVERT(nvarchar(36), [UserId]))
                WHERE [TenantId] = N'' OR [TenantId] IS NULL
                """);
        }
    }
}
