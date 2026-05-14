using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRichQuestionContentModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuestionAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssetType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    MimeType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256Hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SourceRegistryItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceTitle = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    SourceUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    LicenseStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    VerificationStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AltText = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Caption = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    LongDescription = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionAssets_SourceRegistryItems_SourceRegistryItemId",
                        column: x => x.SourceRegistryItemId,
                        principalTable: "SourceRegistryItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionAssets_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuestionStimuli",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    StimulusType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ContentText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourceRegistryItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurriculumNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VerificationStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LicenseStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionStimuli", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionStimuli_CurriculumNodes_CurriculumNodeId",
                        column: x => x.CurriculumNodeId,
                        principalTable: "CurriculumNodes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionStimuli_SourceRegistryItems_SourceRegistryItemId",
                        column: x => x.SourceRegistryItemId,
                        principalTable: "SourceRegistryItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionStimuli_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuestionContentBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlockType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    AltText = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Caption = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    LongDescription = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionContentBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionContentBlocks_QuestionAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "QuestionAssets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionContentBlocks_QuestionItems_QuestionItemId",
                        column: x => x.QuestionItemId,
                        principalTable: "QuestionItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuestionOptionContentBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionOptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlockType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    AltText = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Caption = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionOptionContentBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionOptionContentBlocks_QuestionAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "QuestionAssets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionOptionContentBlocks_QuestionOptions_QuestionOptionId",
                        column: x => x.QuestionOptionId,
                        principalTable: "QuestionOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuestionStimulusLinks",
                columns: table => new
                {
                    QuestionItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionStimulusId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionStimulusLinks", x => new { x.QuestionItemId, x.QuestionStimulusId });
                    table.ForeignKey(
                        name: "FK_QuestionStimulusLinks_QuestionItems_QuestionItemId",
                        column: x => x.QuestionItemId,
                        principalTable: "QuestionItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuestionStimulusLinks_QuestionStimuli_QuestionStimulusId",
                        column: x => x.QuestionStimulusId,
                        principalTable: "QuestionStimuli",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionAssets_OwnerUserId_Sha256Hash_IsDeleted",
                table: "QuestionAssets",
                columns: new[] { "OwnerUserId", "Sha256Hash", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionAssets_SourceRegistryItemId",
                table: "QuestionAssets",
                column: "SourceRegistryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionContentBlocks_AssetId",
                table: "QuestionContentBlocks",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionContentBlocks_QuestionItemId_SortOrder_IsDeleted",
                table: "QuestionContentBlocks",
                columns: new[] { "QuestionItemId", "SortOrder", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionOptionContentBlocks_AssetId",
                table: "QuestionOptionContentBlocks",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionOptionContentBlocks_QuestionOptionId_SortOrder_IsDeleted",
                table: "QuestionOptionContentBlocks",
                columns: new[] { "QuestionOptionId", "SortOrder", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionStimuli_CurriculumNodeId",
                table: "QuestionStimuli",
                column: "CurriculumNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionStimuli_OwnerUserId_StimulusType_IsDeleted",
                table: "QuestionStimuli",
                columns: new[] { "OwnerUserId", "StimulusType", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionStimuli_SourceRegistryItemId",
                table: "QuestionStimuli",
                column: "SourceRegistryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionStimulusLinks_QuestionStimulusId",
                table: "QuestionStimulusLinks",
                column: "QuestionStimulusId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuestionContentBlocks");

            migrationBuilder.DropTable(
                name: "QuestionOptionContentBlocks");

            migrationBuilder.DropTable(
                name: "QuestionStimulusLinks");

            migrationBuilder.DropTable(
                name: "QuestionAssets");

            migrationBuilder.DropTable(
                name: "QuestionStimuli");
        }
    }
}
