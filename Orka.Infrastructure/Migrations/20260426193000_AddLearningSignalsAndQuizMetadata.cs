using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    public partial class AddLearningSignalsAndQuizMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CognitiveType",
                table: "QuizAttempts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Difficulty",
                table: "QuizAttempts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuestionHash",
                table: "QuizAttempts",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuestionId",
                table: "QuizAttempts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "QuizRunId",
                table: "QuizAttempts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SkillTag",
                table: "QuizAttempts",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceRefsJson",
                table: "QuizAttempts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TopicPath",
                table: "QuizAttempts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "QuizRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuizType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TotalQuestions = table.Column<int>(type: "int", nullable: false),
                    CorrectCount = table.Column<int>(type: "int", nullable: false),
                    FailedSkillsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuizRuns_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuizRuns_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuizRuns_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ClassroomSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AudioOverviewJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Transcript = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastSegment = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassroomSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassroomSessions_AudioOverviewJobs_AudioOverviewJobId",
                        column: x => x.AudioOverviewJobId,
                        principalTable: "AudioOverviewJobs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClassroomSessions_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClassroomSessions_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClassroomSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LearningSignals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuizAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SignalType = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SkillTag = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TopicPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Score = table.Column<int>(type: "int", nullable: true),
                    IsPositive = table.Column<bool>(type: "bit", nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningSignals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningSignals_QuizAttempts_QuizAttemptId",
                        column: x => x.QuizAttemptId,
                        principalTable: "QuizAttempts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LearningSignals_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LearningSignals_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LearningSignals_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RemediationPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SkillTag = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LessonMarkdown = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MicroQuizJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemediationPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemediationPlans_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RemediationPlans_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RemediationPlans_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StudyRecommendations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecommendationType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SkillTag = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActionPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDone = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyRecommendations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudyRecommendations_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StudyRecommendations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_QuizRunId",
                table: "QuizAttempts",
                column: "QuizRunId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_UserId_TopicId_QuestionHash",
                table: "QuizAttempts",
                columns: new[] { "UserId", "TopicId", "QuestionHash" });

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_UserId_TopicId_SkillTag",
                table: "QuizAttempts",
                columns: new[] { "UserId", "TopicId", "SkillTag" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassroomSessions_AudioOverviewJobId",
                table: "ClassroomSessions",
                column: "AudioOverviewJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassroomSessions_SessionId",
                table: "ClassroomSessions",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassroomSessions_TopicId",
                table: "ClassroomSessions",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassroomSessions_UserId_TopicId_UpdatedAt",
                table: "ClassroomSessions",
                columns: new[] { "UserId", "TopicId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningSignals_QuizAttemptId",
                table: "LearningSignals",
                column: "QuizAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningSignals_SessionId",
                table: "LearningSignals",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningSignals_TopicId",
                table: "LearningSignals",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningSignals_UserId_TopicId_SignalType_CreatedAt",
                table: "LearningSignals",
                columns: new[] { "UserId", "TopicId", "SignalType", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_QuizRuns_SessionId",
                table: "QuizRuns",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizRuns_TopicId",
                table: "QuizRuns",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizRuns_UserId_TopicId_CreatedAt",
                table: "QuizRuns",
                columns: new[] { "UserId", "TopicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RemediationPlans_SessionId",
                table: "RemediationPlans",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationPlans_TopicId",
                table: "RemediationPlans",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationPlans_UserId",
                table: "RemediationPlans",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyRecommendations_TopicId",
                table: "StudyRecommendations",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyRecommendations_UserId_TopicId_IsDone",
                table: "StudyRecommendations",
                columns: new[] { "UserId", "TopicId", "IsDone" });

            migrationBuilder.AddForeignKey(
                name: "FK_QuizAttempts_QuizRuns_QuizRunId",
                table: "QuizAttempts",
                column: "QuizRunId",
                principalTable: "QuizRuns",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_QuizAttempts_QuizRuns_QuizRunId", table: "QuizAttempts");

            migrationBuilder.DropTable(name: "ClassroomSessions");
            migrationBuilder.DropTable(name: "LearningSignals");
            migrationBuilder.DropTable(name: "RemediationPlans");
            migrationBuilder.DropTable(name: "StudyRecommendations");
            migrationBuilder.DropTable(name: "QuizRuns");

            migrationBuilder.DropIndex(name: "IX_QuizAttempts_QuizRunId", table: "QuizAttempts");
            migrationBuilder.DropIndex(name: "IX_QuizAttempts_UserId_TopicId_QuestionHash", table: "QuizAttempts");
            migrationBuilder.DropIndex(name: "IX_QuizAttempts_UserId_TopicId_SkillTag", table: "QuizAttempts");

            migrationBuilder.DropColumn(name: "CognitiveType", table: "QuizAttempts");
            migrationBuilder.DropColumn(name: "Difficulty", table: "QuizAttempts");
            migrationBuilder.DropColumn(name: "QuestionHash", table: "QuizAttempts");
            migrationBuilder.DropColumn(name: "QuestionId", table: "QuizAttempts");
            migrationBuilder.DropColumn(name: "QuizRunId", table: "QuizAttempts");
            migrationBuilder.DropColumn(name: "SkillTag", table: "QuizAttempts");
            migrationBuilder.DropColumn(name: "SourceRefsJson", table: "QuizAttempts");
            migrationBuilder.DropColumn(name: "TopicPath", table: "QuizAttempts");
        }
    }
}
