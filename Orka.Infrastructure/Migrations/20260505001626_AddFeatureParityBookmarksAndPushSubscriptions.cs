using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFeatureParityBookmarksAndPushSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
IF OBJECT_ID(N'[dbo].[Bookmarks]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Bookmarks] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [TopicId] uniqueidentifier NULL,
        [SessionId] uniqueidentifier NULL,
        [MessageId] uniqueidentifier NULL,
        [LearningSourceId] uniqueidentifier NULL,
        [WikiPageId] uniqueidentifier NULL,
        [ReviewItemId] uniqueidentifier NULL,
        [FlashcardId] uniqueidentifier NULL,
        [Title] nvarchar(256) NOT NULL,
        [Note] nvarchar(max) NULL,
        [Quote] nvarchar(max) NULL,
        [TagsJson] nvarchar(max) NULL,
        [Status] nvarchar(64) NOT NULL CONSTRAINT [DF_Bookmarks_Status] DEFAULT N'active',
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [DeletedAt] datetime2 NULL,
        CONSTRAINT [PK_Bookmarks] PRIMARY KEY ([Id])
    );
END
ELSE
BEGIN
    IF COL_LENGTH('dbo.Bookmarks', 'TopicId') IS NULL EXEC(N'ALTER TABLE [dbo].[Bookmarks] ADD [TopicId] uniqueidentifier NULL');
    IF COL_LENGTH('dbo.Bookmarks', 'SessionId') IS NULL EXEC(N'ALTER TABLE [dbo].[Bookmarks] ADD [SessionId] uniqueidentifier NULL');
    IF COL_LENGTH('dbo.Bookmarks', 'LearningSourceId') IS NULL EXEC(N'ALTER TABLE [dbo].[Bookmarks] ADD [LearningSourceId] uniqueidentifier NULL');
    IF COL_LENGTH('dbo.Bookmarks', 'WikiPageId') IS NULL EXEC(N'ALTER TABLE [dbo].[Bookmarks] ADD [WikiPageId] uniqueidentifier NULL');
    IF COL_LENGTH('dbo.Bookmarks', 'ReviewItemId') IS NULL EXEC(N'ALTER TABLE [dbo].[Bookmarks] ADD [ReviewItemId] uniqueidentifier NULL');
    IF COL_LENGTH('dbo.Bookmarks', 'FlashcardId') IS NULL EXEC(N'ALTER TABLE [dbo].[Bookmarks] ADD [FlashcardId] uniqueidentifier NULL');
    IF COL_LENGTH('dbo.Bookmarks', 'Title') IS NULL EXEC(N'ALTER TABLE [dbo].[Bookmarks] ADD [Title] nvarchar(256) NULL');
    IF COL_LENGTH('dbo.Bookmarks', 'Quote') IS NULL EXEC(N'ALTER TABLE [dbo].[Bookmarks] ADD [Quote] nvarchar(max) NULL');
    IF COL_LENGTH('dbo.Bookmarks', 'TagsJson') IS NULL EXEC(N'ALTER TABLE [dbo].[Bookmarks] ADD [TagsJson] nvarchar(max) NULL');
    IF COL_LENGTH('dbo.Bookmarks', 'Status') IS NULL EXEC(N'ALTER TABLE [dbo].[Bookmarks] ADD [Status] nvarchar(64) NOT NULL CONSTRAINT [DF_Bookmarks_Status] DEFAULT N''active''');
    IF COL_LENGTH('dbo.Bookmarks', 'UpdatedAt') IS NULL EXEC(N'ALTER TABLE [dbo].[Bookmarks] ADD [UpdatedAt] datetime2 NULL');
    IF COL_LENGTH('dbo.Bookmarks', 'DeletedAt') IS NULL EXEC(N'ALTER TABLE [dbo].[Bookmarks] ADD [DeletedAt] datetime2 NULL');

    EXEC(N'UPDATE [dbo].[Bookmarks] SET [Title] = COALESCE(NULLIF([Title], N''''), N''Bookmark'') WHERE [Title] IS NULL OR [Title] = N''''');
    EXEC(N'UPDATE [dbo].[Bookmarks] SET [UpdatedAt] = COALESCE([UpdatedAt], [CreatedAt], SYSUTCDATETIME()) WHERE [UpdatedAt] IS NULL');
    IF EXISTS (
        SELECT 1 FROM sys.columns c
        JOIN sys.types t ON c.user_type_id = t.user_type_id
        WHERE c.object_id = OBJECT_ID(N'[dbo].[Bookmarks]') AND c.name = N'Title' AND c.is_nullable = 1
    )
        EXEC(N'ALTER TABLE [dbo].[Bookmarks] ALTER COLUMN [Title] nvarchar(256) NOT NULL');
    IF EXISTS (
        SELECT 1 FROM sys.columns c
        WHERE c.object_id = OBJECT_ID(N'[dbo].[Bookmarks]') AND c.name = N'UpdatedAt' AND c.is_nullable = 1
    )
        EXEC(N'ALTER TABLE [dbo].[Bookmarks] ALTER COLUMN [UpdatedAt] datetime2 NOT NULL');
END;

IF OBJECT_ID(N'[PushSubscriptions]', N'U') IS NULL
BEGIN
    CREATE TABLE [PushSubscriptions] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Endpoint] nvarchar(450) NOT NULL,
        [P256dh] nvarchar(max) NULL,
        [Auth] nvarchar(max) NULL,
        [DeviceLabel] nvarchar(160) NULL,
        [Status] nvarchar(64) NOT NULL CONSTRAINT [DF_PushSubscriptions_Status] DEFAULT N'active',
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [DeletedAt] datetime2 NULL,
        CONSTRAINT [PK_PushSubscriptions] PRIMARY KEY ([Id])
    );
END;

""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
IF OBJECT_ID(N'[PushSubscriptions]', N'U') IS NOT NULL DROP TABLE [PushSubscriptions];
IF OBJECT_ID(N'[Bookmarks]', N'U') IS NOT NULL DROP TABLE [Bookmarks];
""");
        }
    }
}
