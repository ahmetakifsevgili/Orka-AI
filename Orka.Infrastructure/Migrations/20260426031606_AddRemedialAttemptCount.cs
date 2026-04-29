using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRemedialAttemptCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RemedialAttemptCount",
                table: "Sessions",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemedialAttemptCount",
                table: "Sessions");
        }
    }
}
