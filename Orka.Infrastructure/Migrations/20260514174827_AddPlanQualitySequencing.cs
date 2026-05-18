using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanQualitySequencing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LearningPlanQualitySnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlanRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActiveLessonSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StudentContextSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QualityStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SpecificityScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    SequencingScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    EvidenceAlignmentScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    AssessmentAlignmentScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    TutorAlignmentScore = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    BlockingIssuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WarningIssuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PlanContractJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningPlanQualitySnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningPlanQualitySnapshots_ActiveLessonSnapshots_ActiveLessonSnapshotId",
                        column: x => x.ActiveLessonSnapshotId,
                        principalTable: "ActiveLessonSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LearningPlanQualitySnapshots_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LearningPlanQualitySnapshots_StudentContextSnapshots_StudentContextSnapshotId",
                        column: x => x.StudentContextSnapshotId,
                        principalTable: "StudentContextSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LearningPlanQualitySnapshots_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LearningPlanQualitySnapshots_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LearningPlanQualitySnapshots_ActiveLessonSnapshotId",
                table: "LearningPlanQualitySnapshots",
                column: "ActiveLessonSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningPlanQualitySnapshots_SessionId",
                table: "LearningPlanQualitySnapshots",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningPlanQualitySnapshots_StudentContextSnapshotId",
                table: "LearningPlanQualitySnapshots",
                column: "StudentContextSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningPlanQualitySnapshots_TopicId",
                table: "LearningPlanQualitySnapshots",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningPlanQualitySnapshots_UserId_TopicId_SessionId_CreatedAt",
                table: "LearningPlanQualitySnapshots",
                columns: new[] { "UserId", "TopicId", "SessionId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LearningPlanQualitySnapshots");
        }
    }
}
