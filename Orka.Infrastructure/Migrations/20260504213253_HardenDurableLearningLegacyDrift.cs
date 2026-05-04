using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenDurableLearningLegacyDrift : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('ReviewItems', 'NextReviewAt') IS NOT NULL ALTER TABLE ReviewItems ALTER COLUMN NextReviewAt datetime2 NULL;
                IF COL_LENGTH('DailyChallenges', 'CorrectCount') IS NOT NULL ALTER TABLE DailyChallenges ALTER COLUMN CorrectCount int NULL;
                IF COL_LENGTH('DailyChallenges', 'Score') IS NOT NULL ALTER TABLE DailyChallenges ALTER COLUMN Score int NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
