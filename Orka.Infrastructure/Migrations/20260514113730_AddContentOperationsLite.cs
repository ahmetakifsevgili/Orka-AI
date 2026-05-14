using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContentOperationsLite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuestionContentVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionContentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionContentVersions_QuestionItems_QuestionItemId",
                        column: x => x.QuestionItemId,
                        principalTable: "QuestionItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionContentVersions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuestionReviewWorkflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentStage = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AssignedReviewerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionReviewWorkflows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionReviewWorkflows_QuestionItems_QuestionItemId",
                        column: x => x.QuestionItemId,
                        principalTable: "QuestionItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionReviewWorkflows_Users_AssignedReviewerUserId",
                        column: x => x.AssignedReviewerUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionReviewWorkflows_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionReviewWorkflows_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionReviewWorkflows_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuestionPublishReadinessSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsReadyToPublish = table.Column<bool>(type: "bit", nullable: false),
                    BlockingIssuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WarningIssuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CheckedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CheckedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionPublishReadinessSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionPublishReadinessSnapshots_QuestionItems_QuestionItemId",
                        column: x => x.QuestionItemId,
                        principalTable: "QuestionItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionPublishReadinessSnapshots_QuestionReviewWorkflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "QuestionReviewWorkflows",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionPublishReadinessSnapshots_Users_CheckedByUserId",
                        column: x => x.CheckedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuestionReviewEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionReviewWorkflowId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FromStage = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ToStage = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    SafeNote = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionReviewEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionReviewEvents_QuestionItems_QuestionItemId",
                        column: x => x.QuestionItemId,
                        principalTable: "QuestionItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionReviewEvents_QuestionReviewWorkflows_QuestionReviewWorkflowId",
                        column: x => x.QuestionReviewWorkflowId,
                        principalTable: "QuestionReviewWorkflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuestionReviewEvents_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionContentVersions_CreatedByUserId",
                table: "QuestionContentVersions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionContentVersions_QuestionItemId_VersionNumber_IsDeleted",
                table: "QuestionContentVersions",
                columns: new[] { "QuestionItemId", "VersionNumber", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionPublishReadinessSnapshots_CheckedByUserId",
                table: "QuestionPublishReadinessSnapshots",
                column: "CheckedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionPublishReadinessSnapshots_QuestionItemId_CheckedAt_IsDeleted",
                table: "QuestionPublishReadinessSnapshots",
                columns: new[] { "QuestionItemId", "CheckedAt", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionPublishReadinessSnapshots_WorkflowId",
                table: "QuestionPublishReadinessSnapshots",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionReviewEvents_ActorUserId",
                table: "QuestionReviewEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionReviewEvents_QuestionItemId",
                table: "QuestionReviewEvents",
                column: "QuestionItemId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionReviewEvents_QuestionReviewWorkflowId_CreatedAt_IsDeleted",
                table: "QuestionReviewEvents",
                columns: new[] { "QuestionReviewWorkflowId", "CreatedAt", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionReviewWorkflows_AssignedReviewerUserId",
                table: "QuestionReviewWorkflows",
                column: "AssignedReviewerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionReviewWorkflows_CreatedByUserId",
                table: "QuestionReviewWorkflows",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionReviewWorkflows_OwnerUserId_Status_CurrentStage_IsDeleted",
                table: "QuestionReviewWorkflows",
                columns: new[] { "OwnerUserId", "Status", "CurrentStage", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionReviewWorkflows_QuestionItemId_IsDeleted",
                table: "QuestionReviewWorkflows",
                columns: new[] { "QuestionItemId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionReviewWorkflows_UpdatedByUserId",
                table: "QuestionReviewWorkflows",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuestionContentVersions");

            migrationBuilder.DropTable(
                name: "QuestionPublishReadinessSnapshots");

            migrationBuilder.DropTable(
                name: "QuestionReviewEvents");

            migrationBuilder.DropTable(
                name: "QuestionReviewWorkflows");
        }
    }
}