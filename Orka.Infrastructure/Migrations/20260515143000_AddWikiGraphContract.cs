using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Orka.Infrastructure.Data;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    [DbContext(typeof(OrkaDbContext))]
    [Migration("20260515143000_AddWikiGraphContract")]
    public partial class AddWikiGraphContract : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SessionId",
                table: "WikiPages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConceptGraphSnapshotId",
                table: "WikiPages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlanStepId",
                table: "WikiPages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentWikiPageId",
                table: "WikiPages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PageKey",
                table: "WikiPages",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PageType",
                table: "WikiPages",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "concept");

            migrationBuilder.AddColumn<string>(
                name: "ConceptKey",
                table: "WikiPages",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParentConceptKey",
                table: "WikiPages",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceReadiness",
                table: "WikiPages",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "evidence_insufficient");

            migrationBuilder.AddColumn<string>(
                name: "EvidenceStatus",
                table: "WikiPages",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "evidence_insufficient");

            migrationBuilder.AddColumn<string>(
                name: "SafeSummary",
                table: "WikiPages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetadataJson",
                table: "WikiPages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "WikiPages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SourceBasis",
                table: "WikiBlocks",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "model_assisted");

            migrationBuilder.AddColumn<string>(
                name: "ConceptKey",
                table: "WikiBlocks",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MisconceptionKey",
                table: "WikiBlocks",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "QuizAttemptId",
                table: "WikiBlocks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceEvidenceBundleId",
                table: "WikiBlocks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LearningArtifactId",
                table: "WikiBlocks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TutorTurnStateId",
                table: "WikiBlocks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Visibility",
                table: "WikiBlocks",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "normal");

            migrationBuilder.AddColumn<string>(
                name: "SafetyWarningsJson",
                table: "WikiBlocks",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "WikiBlocks",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "BlockType",
                table: "WikiBlocks",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateTable(
                name: "WikiLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourcePageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetPageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TargetPageKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    LinkType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Strength = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SafeLabel = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WikiLinks", x => x.Id);
                    table.ForeignKey("FK_WikiLinks_Topics_TopicId", x => x.TopicId, "Topics", "Id");
                    table.ForeignKey("FK_WikiLinks_Users_UserId", x => x.UserId, "Users", "Id");
                    table.ForeignKey("FK_WikiLinks_WikiPages_SourcePageId", x => x.SourcePageId, "WikiPages", "Id");
                    table.ForeignKey("FK_WikiLinks_WikiPages_TargetPageId", x => x.TargetPageId, "WikiPages", "Id");
                });

            migrationBuilder.CreateIndex("IX_WikiPages_UserId_TopicId_PageKey_IsDeleted", "WikiPages", new[] { "UserId", "TopicId", "PageKey", "IsDeleted" });
            migrationBuilder.CreateIndex("IX_WikiPages_UserId_TopicId_ConceptKey_IsDeleted", "WikiPages", new[] { "UserId", "TopicId", "ConceptKey", "IsDeleted" });
            migrationBuilder.CreateIndex("IX_WikiBlocks_WikiPageId_BlockType_IsDeleted", "WikiBlocks", new[] { "WikiPageId", "BlockType", "IsDeleted" });
            migrationBuilder.CreateIndex("IX_WikiLinks_UserId_TopicId_LinkType_IsDeleted", "WikiLinks", new[] { "UserId", "TopicId", "LinkType", "IsDeleted" });
            migrationBuilder.CreateIndex("IX_WikiLinks_SourcePageId_TargetPageId_LinkType_IsDeleted", "WikiLinks", new[] { "SourcePageId", "TargetPageId", "LinkType", "IsDeleted" });
            migrationBuilder.CreateIndex("IX_WikiLinks_TopicId", "WikiLinks", "TopicId");
            migrationBuilder.CreateIndex("IX_WikiLinks_TargetPageId", "WikiLinks", "TargetPageId");
            migrationBuilder.CreateIndex("IX_WikiLinks_UserId", "WikiLinks", "UserId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WikiLinks");
            migrationBuilder.DropIndex(name: "IX_WikiPages_UserId_TopicId_PageKey_IsDeleted", table: "WikiPages");
            migrationBuilder.DropIndex(name: "IX_WikiPages_UserId_TopicId_ConceptKey_IsDeleted", table: "WikiPages");
            migrationBuilder.DropIndex(name: "IX_WikiBlocks_WikiPageId_BlockType_IsDeleted", table: "WikiBlocks");

            migrationBuilder.DropColumn(name: "SessionId", table: "WikiPages");
            migrationBuilder.DropColumn(name: "ConceptGraphSnapshotId", table: "WikiPages");
            migrationBuilder.DropColumn(name: "PlanStepId", table: "WikiPages");
            migrationBuilder.DropColumn(name: "ParentWikiPageId", table: "WikiPages");
            migrationBuilder.DropColumn(name: "PageKey", table: "WikiPages");
            migrationBuilder.DropColumn(name: "PageType", table: "WikiPages");
            migrationBuilder.DropColumn(name: "ConceptKey", table: "WikiPages");
            migrationBuilder.DropColumn(name: "ParentConceptKey", table: "WikiPages");
            migrationBuilder.DropColumn(name: "SourceReadiness", table: "WikiPages");
            migrationBuilder.DropColumn(name: "EvidenceStatus", table: "WikiPages");
            migrationBuilder.DropColumn(name: "SafeSummary", table: "WikiPages");
            migrationBuilder.DropColumn(name: "MetadataJson", table: "WikiPages");
            migrationBuilder.DropColumn(name: "IsDeleted", table: "WikiPages");

            migrationBuilder.DropColumn(name: "SourceBasis", table: "WikiBlocks");
            migrationBuilder.DropColumn(name: "ConceptKey", table: "WikiBlocks");
            migrationBuilder.DropColumn(name: "MisconceptionKey", table: "WikiBlocks");
            migrationBuilder.DropColumn(name: "QuizAttemptId", table: "WikiBlocks");
            migrationBuilder.DropColumn(name: "SourceEvidenceBundleId", table: "WikiBlocks");
            migrationBuilder.DropColumn(name: "LearningArtifactId", table: "WikiBlocks");
            migrationBuilder.DropColumn(name: "TutorTurnStateId", table: "WikiBlocks");
            migrationBuilder.DropColumn(name: "Visibility", table: "WikiBlocks");
            migrationBuilder.DropColumn(name: "SafetyWarningsJson", table: "WikiBlocks");
            migrationBuilder.DropColumn(name: "IsDeleted", table: "WikiBlocks");

            migrationBuilder.AlterColumn<string>(
                name: "BlockType",
                table: "WikiBlocks",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);
        }
    }
}
