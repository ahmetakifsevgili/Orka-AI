using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCurriculumGraphHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "CurriculumVersions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeprecatedAt",
                table: "CurriculumVersions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeprecatedReason",
                table: "CurriculumVersions",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupersededByCurriculumVersionId",
                table: "CurriculumVersions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AnchorText",
                table: "CurriculumOutcomeMappings",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Clause",
                table: "CurriculumOutcomeMappings",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceUrl",
                table: "CurriculumOutcomeMappings",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PageNumber",
                table: "CurriculumOutcomeMappings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewStatus",
                table: "CurriculumOutcomeMappings",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "draft");

            migrationBuilder.AddColumn<string>(
                name: "SectionTitle",
                table: "CurriculumOutcomeMappings",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceLocator",
                table: "CurriculumNodes",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumVersions_ExamDefinitionId_OwnerUserId_SourceRegistryItemId_Status_IsDeleted",
                table: "CurriculumVersions",
                columns: new[] { "ExamDefinitionId", "OwnerUserId", "SourceRegistryItemId", "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumVersions_SupersededByCurriculumVersionId",
                table: "CurriculumVersions",
                column: "SupersededByCurriculumVersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_CurriculumVersions_CurriculumVersions_SupersededByCurriculumVersionId",
                table: "CurriculumVersions",
                column: "SupersededByCurriculumVersionId",
                principalTable: "CurriculumVersions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CurriculumVersions_CurriculumVersions_SupersededByCurriculumVersionId",
                table: "CurriculumVersions");

            migrationBuilder.DropIndex(
                name: "IX_CurriculumVersions_ExamDefinitionId_OwnerUserId_SourceRegistryItemId_Status_IsDeleted",
                table: "CurriculumVersions");

            migrationBuilder.DropIndex(
                name: "IX_CurriculumVersions_SupersededByCurriculumVersionId",
                table: "CurriculumVersions");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "CurriculumVersions");

            migrationBuilder.DropColumn(
                name: "DeprecatedAt",
                table: "CurriculumVersions");

            migrationBuilder.DropColumn(
                name: "DeprecatedReason",
                table: "CurriculumVersions");

            migrationBuilder.DropColumn(
                name: "SupersededByCurriculumVersionId",
                table: "CurriculumVersions");

            migrationBuilder.DropColumn(
                name: "AnchorText",
                table: "CurriculumOutcomeMappings");

            migrationBuilder.DropColumn(
                name: "Clause",
                table: "CurriculumOutcomeMappings");

            migrationBuilder.DropColumn(
                name: "EvidenceUrl",
                table: "CurriculumOutcomeMappings");

            migrationBuilder.DropColumn(
                name: "PageNumber",
                table: "CurriculumOutcomeMappings");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "CurriculumOutcomeMappings");

            migrationBuilder.DropColumn(
                name: "SectionTitle",
                table: "CurriculumOutcomeMappings");

            migrationBuilder.DropColumn(
                name: "SourceLocator",
                table: "CurriculumNodes");
        }
    }
}
