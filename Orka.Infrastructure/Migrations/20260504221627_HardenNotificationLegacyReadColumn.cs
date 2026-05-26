using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenNotificationLegacyReadColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('Notifications', 'IsRead') IS NOT NULL
                BEGIN
                    EXEC(N'UPDATE Notifications SET IsRead = COALESCE(IsRead, CASE WHEN Status = ''read'' THEN 1 ELSE 0 END) WHERE IsRead IS NULL');
                    EXEC(N'ALTER TABLE Notifications ALTER COLUMN IsRead bit NULL');
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
