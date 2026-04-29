using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    public partial class AddNotebookLmSources : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AudioOverviewJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Script = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SpeakersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AudioBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AudioOverviewJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AudioOverviewJobs_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AudioOverviewJobs_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AudioOverviewJobs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LearningSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PageCount = table.Column<int>(type: "int", nullable: false),
                    ChunkCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningSources_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LearningSources_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LearningSources_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SourceChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LearningSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PageNumber = table.Column<int>(type: "int", nullable: false),
                    ChunkIndex = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmbeddingJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HighlightHint = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceChunks_LearningSources_LearningSourceId",
                        column: x => x.LearningSourceId,
                        principalTable: "LearningSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AudioOverviewJobs_SessionId",
                table: "AudioOverviewJobs",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AudioOverviewJobs_TopicId",
                table: "AudioOverviewJobs",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_AudioOverviewJobs_UserId_TopicId_CreatedAt",
                table: "AudioOverviewJobs",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningSources_SessionId",
                table: "LearningSources",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningSources_TopicId",
                table: "LearningSources",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningSources_UserId_SessionId",
                table: "LearningSources",
                columns: new[] { "UserId", "SessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningSources_UserId_TopicId",
                table: "LearningSources",
                columns: new[] { "UserId", "TopicId" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceChunks_LearningSourceId_PageNumber_ChunkIndex",
                table: "SourceChunks",
                columns: new[] { "LearningSourceId", "PageNumber", "ChunkIndex" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AudioOverviewJobs");
            migrationBuilder.DropTable(name: "SourceChunks");
            migrationBuilder.DropTable(name: "LearningSources");
        }
    }
}
