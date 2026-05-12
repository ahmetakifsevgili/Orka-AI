using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddV1CleanupStorageAndCostContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "FileSizeBytes",
                table: "LearningSources",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "TopicId",
                table: "CostRecords",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CostRecords_TopicId_OccurredAt",
                table: "CostRecords",
                columns: new[] { "TopicId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CostRecords_TopicId_OccurredAt",
                table: "CostRecords");

            migrationBuilder.DropColumn(
                name: "FileSizeBytes",
                table: "LearningSources");

            migrationBuilder.DropColumn(
                name: "TopicId",
                table: "CostRecords");
        }
    }
}
