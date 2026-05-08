using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceRagQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SourceQualityReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QualityStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RetrievalHealthStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CitationCoverageStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CitationSupportStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RetrievalRunCount = table.Column<int>(type: "int", nullable: false),
                    EmptyRunCount = table.Column<int>(type: "int", nullable: false),
                    CitationCheckCount = table.Column<int>(type: "int", nullable: false),
                    UnsupportedCitationCount = table.Column<int>(type: "int", nullable: false),
                    CitationMissingCount = table.Column<int>(type: "int", nullable: false),
                    AverageContextRelevance = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    CitationCoverage = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    ReportJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceQualityReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceQualityReports_LearningSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "LearningSources",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SourceQualityReports_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SourceQualityReports_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SourceRetrievalRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Query = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RetrievalScope = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestedTopK = table.Column<int>(type: "int", nullable: false),
                    RetrievedCount = table.Column<int>(type: "int", nullable: false),
                    IsEmpty = table.Column<bool>(type: "bit", nullable: false),
                    MaxScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    AverageScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    QualityStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceRetrievalRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceRetrievalRuns_LearningSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "LearningSources",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SourceRetrievalRuns_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SourceRetrievalRuns_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SourceRetrievalRuns_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SourceCitationChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceRetrievalRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceChunkId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CitationId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PageNumber = table.Column<int>(type: "int", nullable: true),
                    ChunkIndex = table.Column<int>(type: "int", nullable: true),
                    Answer = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClaimText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CheckStatus = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Confidence = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceCitationChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceCitationChecks_LearningSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "LearningSources",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SourceCitationChecks_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SourceCitationChecks_SourceChunks_SourceChunkId",
                        column: x => x.SourceChunkId,
                        principalTable: "SourceChunks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SourceCitationChecks_SourceRetrievalRuns_SourceRetrievalRunId",
                        column: x => x.SourceRetrievalRunId,
                        principalTable: "SourceRetrievalRuns",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SourceCitationChecks_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SourceCitationChecks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SourceRetrievalItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceRetrievalRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceChunkId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PageNumber = table.Column<int>(type: "int", nullable: false),
                    ChunkIndex = table.Column<int>(type: "int", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    EmbeddingScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    LexicalScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    FusedScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    QualityStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Snippet = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceRetrievalItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceRetrievalItems_LearningSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "LearningSources",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SourceRetrievalItems_SourceChunks_SourceChunkId",
                        column: x => x.SourceChunkId,
                        principalTable: "SourceChunks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SourceRetrievalItems_SourceRetrievalRuns_SourceRetrievalRunId",
                        column: x => x.SourceRetrievalRunId,
                        principalTable: "SourceRetrievalRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceCitationChecks_SessionId",
                table: "SourceCitationChecks",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceCitationChecks_SourceChunkId",
                table: "SourceCitationChecks",
                column: "SourceChunkId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceCitationChecks_SourceId",
                table: "SourceCitationChecks",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceCitationChecks_SourceRetrievalRunId_CheckStatus",
                table: "SourceCitationChecks",
                columns: new[] { "SourceRetrievalRunId", "CheckStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceCitationChecks_TopicId",
                table: "SourceCitationChecks",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceCitationChecks_UserId_TopicId_CreatedAt",
                table: "SourceCitationChecks",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceQualityReports_SourceId",
                table: "SourceQualityReports",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceQualityReports_TopicId",
                table: "SourceQualityReports",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceQualityReports_UserId_TopicId_GeneratedAt",
                table: "SourceQualityReports",
                columns: new[] { "UserId", "TopicId", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceRetrievalItems_SourceChunkId",
                table: "SourceRetrievalItems",
                column: "SourceChunkId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceRetrievalItems_SourceId",
                table: "SourceRetrievalItems",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceRetrievalItems_SourceRetrievalRunId_Rank",
                table: "SourceRetrievalItems",
                columns: new[] { "SourceRetrievalRunId", "Rank" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceRetrievalRuns_SessionId",
                table: "SourceRetrievalRuns",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceRetrievalRuns_SourceId",
                table: "SourceRetrievalRuns",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceRetrievalRuns_TopicId",
                table: "SourceRetrievalRuns",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceRetrievalRuns_UserId_SourceId_CreatedAt",
                table: "SourceRetrievalRuns",
                columns: new[] { "UserId", "SourceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceRetrievalRuns_UserId_TopicId_CreatedAt",
                table: "SourceRetrievalRuns",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SourceCitationChecks");

            migrationBuilder.DropTable(
                name: "SourceQualityReports");

            migrationBuilder.DropTable(
                name: "SourceRetrievalItems");

            migrationBuilder.DropTable(
                name: "SourceRetrievalRuns");
        }
    }
}
