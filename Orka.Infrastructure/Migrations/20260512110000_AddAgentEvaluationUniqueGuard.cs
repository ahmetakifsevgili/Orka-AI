using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    [Migration("20260512110000_AddAgentEvaluationUniqueGuard")]
    public partial class AddAgentEvaluationUniqueGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [AgentEvaluations]
                SET [AgentRole] = LEFT(LTRIM(RTRIM(COALESCE(NULLIF([AgentRole], N''), N'Unknown'))), 128);
                """);

            migrationBuilder.Sql("""
                WITH ranked AS
                (
                    SELECT
                        [Id],
                        ROW_NUMBER() OVER (
                            PARTITION BY [MessageId], [AgentRole]
                            ORDER BY [CreatedAt] ASC, [Id] ASC
                        ) AS rn
                    FROM [AgentEvaluations]
                )
                DELETE FROM ranked
                WHERE rn > 1;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "AgentRole",
                table: "AgentEvaluations",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_AgentEvaluations_MessageId_AgentRole",
                table: "AgentEvaluations",
                columns: new[] { "MessageId", "AgentRole" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AgentEvaluations_MessageId_AgentRole",
                table: "AgentEvaluations");

            migrationBuilder.AlterColumn<string>(
                name: "AgentRole",
                table: "AgentEvaluations",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);
        }
    }
}
