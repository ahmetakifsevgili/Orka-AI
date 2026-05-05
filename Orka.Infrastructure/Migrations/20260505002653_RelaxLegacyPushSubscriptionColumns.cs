using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RelaxLegacyPushSubscriptionColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
IF OBJECT_ID(N'[dbo].[PushSubscriptions]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.PushSubscriptions', 'FcmToken') IS NOT NULL
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.default_constraints dc
            JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE dc.parent_object_id = OBJECT_ID(N'[dbo].[PushSubscriptions]') AND c.name = N'FcmToken'
        )
            ALTER TABLE [dbo].[PushSubscriptions] ADD CONSTRAINT [DF_PushSubscriptions_FcmToken] DEFAULT N'' FOR [FcmToken];
    END;

    IF COL_LENGTH('dbo.PushSubscriptions', 'DeviceType') IS NOT NULL
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.default_constraints dc
            JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE dc.parent_object_id = OBJECT_ID(N'[dbo].[PushSubscriptions]') AND c.name = N'DeviceType'
        )
            ALTER TABLE [dbo].[PushSubscriptions] ADD CONSTRAINT [DF_PushSubscriptions_DeviceType] DEFAULT N'web' FOR [DeviceType];
    END;

    IF COL_LENGTH('dbo.PushSubscriptions', 'LastSeenAt') IS NOT NULL
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.default_constraints dc
            JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE dc.parent_object_id = OBJECT_ID(N'[dbo].[PushSubscriptions]') AND c.name = N'LastSeenAt'
        )
            ALTER TABLE [dbo].[PushSubscriptions] ADD CONSTRAINT [DF_PushSubscriptions_LastSeenAt] DEFAULT SYSUTCDATETIME() FOR [LastSeenAt];
    END;

    IF COL_LENGTH('dbo.PushSubscriptions', 'IsRevoked') IS NOT NULL
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.default_constraints dc
            JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE dc.parent_object_id = OBJECT_ID(N'[dbo].[PushSubscriptions]') AND c.name = N'IsRevoked'
        )
            ALTER TABLE [dbo].[PushSubscriptions] ADD CONSTRAINT [DF_PushSubscriptions_IsRevoked] DEFAULT CONVERT(bit, 0) FOR [IsRevoked];
    END;
END;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Additive compatibility defaults only.
        }
    }
}
