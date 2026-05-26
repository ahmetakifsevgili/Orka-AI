using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenNotificationLegacyMessageColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('Notifications', 'Message') IS NOT NULL
                BEGIN
                    EXEC(N'UPDATE Notifications SET [Message] = COALESCE([Message], Body, Title, '''') WHERE [Message] IS NULL');
                    EXEC(N'ALTER TABLE Notifications ALTER COLUMN [Message] nvarchar(max) NULL');
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
