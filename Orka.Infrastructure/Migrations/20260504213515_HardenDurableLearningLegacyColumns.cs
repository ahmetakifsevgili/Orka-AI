using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenDurableLearningLegacyColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('ReviewItems', 'LastReviewQuality') IS NOT NULL ALTER TABLE ReviewItems ALTER COLUMN LastReviewQuality int NULL;
                IF COL_LENGTH('ReviewItems', 'ReviewCount') IS NOT NULL ALTER TABLE ReviewItems ALTER COLUMN ReviewCount int NULL;
                IF COL_LENGTH('ReviewItems', 'Type') IS NOT NULL ALTER TABLE ReviewItems ALTER COLUMN [Type] nvarchar(max) NULL;
                IF COL_LENGTH('ReviewItems', 'Title') IS NOT NULL ALTER TABLE ReviewItems ALTER COLUMN Title nvarchar(max) NULL;
                IF COL_LENGTH('ReviewItems', 'Description') IS NOT NULL ALTER TABLE ReviewItems ALTER COLUMN [Description] nvarchar(max) NULL;
                IF COL_LENGTH('UserBadges', 'Id') IS NULL ALTER TABLE UserBadges ADD Id uniqueidentifier NOT NULL CONSTRAINT DF_UserBadges_Id DEFAULT NEWID();
                IF COL_LENGTH('UserBadges', 'MetadataJson') IS NULL ALTER TABLE UserBadges ADD MetadataJson nvarchar(max) NULL;
                IF COL_LENGTH('UserBadges', 'SourceEventId') IS NULL ALTER TABLE UserBadges ADD SourceEventId uniqueidentifier NULL;
                IF COL_LENGTH('DailyChallengeSubmissions', 'Id') IS NULL ALTER TABLE DailyChallengeSubmissions ADD Id uniqueidentifier NOT NULL CONSTRAINT DF_DailyChallengeSubmissions_Id DEFAULT NEWID();
                IF COL_LENGTH('DailyChallengeSubmissions', 'MetadataJson') IS NULL ALTER TABLE DailyChallengeSubmissions ADD MetadataJson nvarchar(max) NULL;
                IF COL_LENGTH('DailyChallengeSubmissions', 'XpEventId') IS NULL ALTER TABLE DailyChallengeSubmissions ADD XpEventId uniqueidentifier NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
