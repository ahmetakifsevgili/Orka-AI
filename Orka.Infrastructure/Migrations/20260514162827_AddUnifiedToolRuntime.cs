using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUnifiedToolRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ToolRuntimeTraces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActiveLessonSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StudentContextSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TutorTurnStateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TutorActionTraceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ToolId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Caller = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RiskLevel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CanGroundClaims = table.Column<bool>(type: "bit", nullable: false),
                    InputSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SafeResultSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FallbackReason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolRuntimeTraces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ToolRuntimeTraces_ActiveLessonSnapshots_ActiveLessonSnapshotId",
                        column: x => x.ActiveLessonSnapshotId,
                        principalTable: "ActiveLessonSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ToolRuntimeTraces_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ToolRuntimeTraces_StudentContextSnapshots_StudentContextSnapshotId",
                        column: x => x.StudentContextSnapshotId,
                        principalTable: "StudentContextSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ToolRuntimeTraces_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ToolRuntimeTraces_TutorActionTraces_TutorActionTraceId",
                        column: x => x.TutorActionTraceId,
                        principalTable: "TutorActionTraces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ToolRuntimeTraces_TutorTurnStates_TutorTurnStateId",
                        column: x => x.TutorTurnStateId,
                        principalTable: "TutorTurnStates",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ToolRuntimeTraces_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ToolRuntimeTraces_ActiveLessonSnapshotId",
                table: "ToolRuntimeTraces",
                column: "ActiveLessonSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ToolRuntimeTraces_SessionId",
                table: "ToolRuntimeTraces",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ToolRuntimeTraces_StudentContextSnapshotId",
                table: "ToolRuntimeTraces",
                column: "StudentContextSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ToolRuntimeTraces_TopicId",
                table: "ToolRuntimeTraces",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_ToolRuntimeTraces_TutorActionTraceId",
                table: "ToolRuntimeTraces",
                column: "TutorActionTraceId");

            migrationBuilder.CreateIndex(
                name: "IX_ToolRuntimeTraces_TutorTurnStateId",
                table: "ToolRuntimeTraces",
                column: "TutorTurnStateId");

            migrationBuilder.CreateIndex(
                name: "IX_ToolRuntimeTraces_UserId_CreatedAt",
                table: "ToolRuntimeTraces",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ToolRuntimeTraces_UserId_SessionId_CreatedAt",
                table: "ToolRuntimeTraces",
                columns: new[] { "UserId", "SessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ToolRuntimeTraces_UserId_TopicId_CreatedAt",
                table: "ToolRuntimeTraces",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ToolRuntimeTraces");
        }
    }
}
