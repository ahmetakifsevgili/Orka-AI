using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRuntimeTelemetryAndCostRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CostRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AgentRole = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EstimatedTokens = table.Column<int>(type: "int", nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ToolTelemetryEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ToolId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CapabilityStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FallbackUsed = table.Column<bool>(type: "bit", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolTelemetryEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CostRecords_Provider_Model_OccurredAt",
                table: "CostRecords",
                columns: new[] { "Provider", "Model", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CostRecords_UserId_OccurredAt",
                table: "CostRecords",
                columns: new[] { "UserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ToolTelemetryEvents_ToolId_OccurredAt",
                table: "ToolTelemetryEvents",
                columns: new[] { "ToolId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ToolTelemetryEvents_UserId_OccurredAt",
                table: "ToolTelemetryEvents",
                columns: new[] { "UserId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CostRecords");

            migrationBuilder.DropTable(
                name: "ToolTelemetryEvents");
        }
    }
}
