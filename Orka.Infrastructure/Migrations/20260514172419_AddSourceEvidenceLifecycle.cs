using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceEvidenceLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SourceEvidenceBundles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BundleHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EvidenceStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceCount = table.Column<int>(type: "int", nullable: false),
                    ReadySourceCount = table.Column<int>(type: "int", nullable: false),
                    ChunkCount = table.Column<int>(type: "int", nullable: false),
                    CitationCoverage = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    UnsupportedCitationCount = table.Column<int>(type: "int", nullable: false),
                    StaleEvidenceCount = table.Column<int>(type: "int", nullable: false),
                    DeletedEvidenceCount = table.Column<int>(type: "int", nullable: false),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceEvidenceBundles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceEvidenceBundles_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SourceEvidenceBundles_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SourceEvidenceBundles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SourceLifecycleEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    SafeSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceLifecycleEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceLifecycleEvents_LearningSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "LearningSources",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SourceLifecycleEvents_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SourceLifecycleEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WikiKnowledgeNotebookSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvidenceStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceCoverage = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    ConceptCoverage = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    SectionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceWarningsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WikiKnowledgeNotebookSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WikiKnowledgeNotebookSnapshots_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WikiKnowledgeNotebookSnapshots_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidenceBundles_SessionId",
                table: "SourceEvidenceBundles",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidenceBundles_TopicId",
                table: "SourceEvidenceBundles",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceEvidenceBundles_UserId_TopicId_SessionId_CreatedAt",
                table: "SourceEvidenceBundles",
                columns: new[] { "UserId", "TopicId", "SessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceLifecycleEvents_SourceId",
                table: "SourceLifecycleEvents",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceLifecycleEvents_TopicId",
                table: "SourceLifecycleEvents",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceLifecycleEvents_UserId_SourceId_CreatedAt",
                table: "SourceLifecycleEvents",
                columns: new[] { "UserId", "SourceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceLifecycleEvents_UserId_TopicId_CreatedAt",
                table: "SourceLifecycleEvents",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WikiKnowledgeNotebookSnapshots_TopicId",
                table: "WikiKnowledgeNotebookSnapshots",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_WikiKnowledgeNotebookSnapshots_UserId_TopicId_CreatedAt",
                table: "WikiKnowledgeNotebookSnapshots",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SourceEvidenceBundles");

            migrationBuilder.DropTable(
                name: "SourceLifecycleEvents");

            migrationBuilder.DropTable(
                name: "WikiKnowledgeNotebookSnapshots");
        }
    }
}
