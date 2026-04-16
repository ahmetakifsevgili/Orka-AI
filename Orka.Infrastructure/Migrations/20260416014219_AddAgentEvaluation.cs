using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentEvaluation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentRole = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserInput = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AgentResponse = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EvaluationScore = table.Column<int>(type: "int", nullable: false),
                    EvaluatorFeedback = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentEvaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentEvaluations_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgentEvaluations_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgentEvaluations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentEvaluations_MessageId",
                table: "AgentEvaluations",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentEvaluations_SessionId",
                table: "AgentEvaluations",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentEvaluations_UserId",
                table: "AgentEvaluations",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentEvaluations");
        }
    }
}
