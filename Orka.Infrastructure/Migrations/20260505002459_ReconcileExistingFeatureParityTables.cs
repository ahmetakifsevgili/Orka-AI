using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileExistingFeatureParityTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
IF OBJECT_ID(N'[dbo].[Bookmarks]', N'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'[dbo].[Bookmarks]') AND name = N'MessageId' AND is_nullable = 0
    )
        EXEC(N'ALTER TABLE [dbo].[Bookmarks] ALTER COLUMN [MessageId] uniqueidentifier NULL');
END;

IF OBJECT_ID(N'[dbo].[PushSubscriptions]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.PushSubscriptions', 'Endpoint') IS NULL EXEC(N'ALTER TABLE [dbo].[PushSubscriptions] ADD [Endpoint] nvarchar(450) NULL');
    IF COL_LENGTH('dbo.PushSubscriptions', 'P256dh') IS NULL EXEC(N'ALTER TABLE [dbo].[PushSubscriptions] ADD [P256dh] nvarchar(max) NULL');
    IF COL_LENGTH('dbo.PushSubscriptions', 'Auth') IS NULL EXEC(N'ALTER TABLE [dbo].[PushSubscriptions] ADD [Auth] nvarchar(max) NULL');
    IF COL_LENGTH('dbo.PushSubscriptions', 'DeviceLabel') IS NULL EXEC(N'ALTER TABLE [dbo].[PushSubscriptions] ADD [DeviceLabel] nvarchar(160) NULL');
    IF COL_LENGTH('dbo.PushSubscriptions', 'Status') IS NULL EXEC(N'ALTER TABLE [dbo].[PushSubscriptions] ADD [Status] nvarchar(64) NULL');
    IF COL_LENGTH('dbo.PushSubscriptions', 'UpdatedAt') IS NULL EXEC(N'ALTER TABLE [dbo].[PushSubscriptions] ADD [UpdatedAt] datetime2 NULL');
    IF COL_LENGTH('dbo.PushSubscriptions', 'DeletedAt') IS NULL EXEC(N'ALTER TABLE [dbo].[PushSubscriptions] ADD [DeletedAt] datetime2 NULL');

    IF COL_LENGTH('dbo.PushSubscriptions', 'FcmToken') IS NOT NULL
        EXEC(N'UPDATE [dbo].[PushSubscriptions] SET [Endpoint] = COALESCE([Endpoint], [FcmToken]) WHERE [Endpoint] IS NULL');

    IF COL_LENGTH('dbo.PushSubscriptions', 'IsRevoked') IS NOT NULL
        EXEC(N'UPDATE [dbo].[PushSubscriptions] SET [Status] = CASE WHEN [IsRevoked] = 1 THEN N''deleted'' ELSE N''active'' END WHERE [Status] IS NULL');
    ELSE
        EXEC(N'UPDATE [dbo].[PushSubscriptions] SET [Status] = COALESCE([Status], N''active'') WHERE [Status] IS NULL');

    EXEC(N'UPDATE [dbo].[PushSubscriptions] SET [Endpoint] = COALESCE([Endpoint], CONVERT(nvarchar(36), [Id])) WHERE [Endpoint] IS NULL');
    EXEC(N'UPDATE [dbo].[PushSubscriptions] SET [UpdatedAt] = COALESCE([UpdatedAt], [CreatedAt], SYSUTCDATETIME()) WHERE [UpdatedAt] IS NULL');

    IF EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'[dbo].[PushSubscriptions]') AND name = N'Endpoint' AND is_nullable = 1
    )
        EXEC(N'ALTER TABLE [dbo].[PushSubscriptions] ALTER COLUMN [Endpoint] nvarchar(450) NOT NULL');

    IF EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'[dbo].[PushSubscriptions]') AND name = N'Status' AND is_nullable = 1
    )
        EXEC(N'ALTER TABLE [dbo].[PushSubscriptions] ALTER COLUMN [Status] nvarchar(64) NOT NULL');

    IF EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'[dbo].[PushSubscriptions]') AND name = N'UpdatedAt' AND is_nullable = 1
    )
        EXEC(N'ALTER TABLE [dbo].[PushSubscriptions] ALTER COLUMN [UpdatedAt] datetime2 NOT NULL');
END;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Additive bridge only. Down intentionally preserves reconciled data.
        }
    }
}
