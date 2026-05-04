using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableLearningContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('Topics', 'MetadataJson') IS NULL ALTER TABLE Topics ADD MetadataJson nvarchar(max) NULL;
                IF COL_LENGTH('Topics', 'PlanIntent') IS NULL ALTER TABLE Topics ADD PlanIntent nvarchar(450) NULL;
                IF COL_LENGTH('SourceChunks', 'IsDeleted') IS NULL ALTER TABLE SourceChunks ADD IsDeleted bit NOT NULL CONSTRAINT DF_SourceChunks_IsDeleted DEFAULT(0);
                IF COL_LENGTH('Messages', 'MetadataJson') IS NULL ALTER TABLE Messages ADD MetadataJson nvarchar(max) NULL;
                IF COL_LENGTH('LearningSources', 'DeletedAt') IS NULL ALTER TABLE LearningSources ADD DeletedAt datetime2 NULL;
                IF COL_LENGTH('LearningSources', 'DeletedByUserId') IS NULL ALTER TABLE LearningSources ADD DeletedByUserId uniqueidentifier NULL;
                IF COL_LENGTH('LearningSources', 'IsDeleted') IS NULL ALTER TABLE LearningSources ADD IsDeleted bit NOT NULL CONSTRAINT DF_LearningSources_IsDeleted DEFAULT(0);
                IF COL_LENGTH('LearningSources', 'Version') IS NULL ALTER TABLE LearningSources ADD Version int NOT NULL CONSTRAINT DF_LearningSources_Version DEFAULT(1);
                """);

            migrationBuilder.Sql("""
                UPDATE LearningSources SET Version = 1 WHERE Version = 0;
                UPDATE Topics SET PlanIntent = SUBSTRING(Category, 6, 100) WHERE PlanIntent IS NULL AND Category LIKE 'Plan:%';
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[Badges]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [Badges] (
                        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_Badges] PRIMARY KEY,
                        [Code] nvarchar(450) NOT NULL,
                        [Name] nvarchar(max) NOT NULL,
                        [Description] nvarchar(max) NOT NULL,
                        [IconKey] nvarchar(max) NOT NULL,
                        [RuleType] nvarchar(max) NOT NULL,
                        [Threshold] int NULL,
                        [IsActive] bit NOT NULL,
                        [CreatedAt] datetime2 NOT NULL,
                        [MetadataJson] nvarchar(max) NULL
                    );
                END;
                IF COL_LENGTH('Badges', 'Code') IS NULL ALTER TABLE Badges ADD Code nvarchar(450) NOT NULL CONSTRAINT DF_Badges_Code DEFAULT(CONVERT(nvarchar(36), NEWID()));
                IF COL_LENGTH('Badges', 'Name') IS NULL ALTER TABLE Badges ADD Name nvarchar(max) NOT NULL CONSTRAINT DF_Badges_Name DEFAULT('Badge');
                IF COL_LENGTH('Badges', 'Description') IS NULL ALTER TABLE Badges ADD Description nvarchar(max) NOT NULL CONSTRAINT DF_Badges_Description DEFAULT('');
                IF COL_LENGTH('Badges', 'IconKey') IS NULL ALTER TABLE Badges ADD IconKey nvarchar(max) NOT NULL CONSTRAINT DF_Badges_IconKey DEFAULT('award');
                IF COL_LENGTH('Badges', 'RuleType') IS NULL ALTER TABLE Badges ADD RuleType nvarchar(max) NOT NULL CONSTRAINT DF_Badges_RuleType DEFAULT('manual');
                IF COL_LENGTH('Badges', 'Threshold') IS NULL ALTER TABLE Badges ADD Threshold int NULL;
                IF COL_LENGTH('Badges', 'Threshold') IS NOT NULL ALTER TABLE Badges ALTER COLUMN Threshold int NULL;
                IF COL_LENGTH('Badges', 'IsActive') IS NULL ALTER TABLE Badges ADD IsActive bit NOT NULL CONSTRAINT DF_Badges_IsActive DEFAULT(1);
                IF COL_LENGTH('Badges', 'CreatedAt') IS NULL ALTER TABLE Badges ADD CreatedAt datetime2 NOT NULL CONSTRAINT DF_Badges_CreatedAt DEFAULT SYSUTCDATETIME();
                IF COL_LENGTH('Badges', 'MetadataJson') IS NULL ALTER TABLE Badges ADD MetadataJson nvarchar(max) NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Badges_Code' AND object_id = OBJECT_ID('Badges'))
                    CREATE UNIQUE INDEX [IX_Badges_Code] ON [Badges] ([Code]);
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[XpEvents]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [XpEvents] (
                        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_XpEvents] PRIMARY KEY,
                        [UserId] uniqueidentifier NOT NULL,
                        [EventKey] nvarchar(450) NOT NULL,
                        [EventType] nvarchar(max) NOT NULL,
                        [XpDelta] int NOT NULL,
                        [RelatedEntityType] nvarchar(max) NULL,
                        [RelatedEntityId] uniqueidentifier NULL,
                        [CreatedAt] datetime2 NOT NULL,
                        [MetadataJson] nvarchar(max) NULL,
                        CONSTRAINT [FK_XpEvents_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id])
                    );
                END;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_XpEvents_UserId_EventKey' AND object_id = OBJECT_ID('XpEvents'))
                    CREATE UNIQUE INDEX [IX_XpEvents_UserId_EventKey] ON [XpEvents] ([UserId], [EventKey]);
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[Flashcards]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [Flashcards] (
                        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_Flashcards] PRIMARY KEY,
                        [UserId] uniqueidentifier NOT NULL,
                        [TopicId] uniqueidentifier NULL,
                        [LearningSourceId] uniqueidentifier NULL,
                        [WikiPageId] uniqueidentifier NULL,
                        [QuizAttemptId] uniqueidentifier NULL,
                        [Front] nvarchar(max) NOT NULL,
                        [Back] nvarchar(max) NOT NULL,
                        [Hint] nvarchar(max) NULL,
                        [SkillTag] nvarchar(max) NULL,
                        [ConceptTag] nvarchar(max) NULL,
                        [LearningObjective] nvarchar(max) NULL,
                        [Difficulty] nvarchar(max) NULL,
                        [Status] nvarchar(450) NOT NULL,
                        [CreatedFrom] nvarchar(max) NOT NULL,
                        [CreatedAt] datetime2 NOT NULL,
                        [UpdatedAt] datetime2 NOT NULL,
                        [LastReviewedAt] datetime2 NULL,
                        [MetadataJson] nvarchar(max) NULL,
                        CONSTRAINT [FK_Flashcards_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]),
                        CONSTRAINT [FK_Flashcards_Topics_TopicId] FOREIGN KEY ([TopicId]) REFERENCES [Topics] ([Id]),
                        CONSTRAINT [FK_Flashcards_LearningSources_LearningSourceId] FOREIGN KEY ([LearningSourceId]) REFERENCES [LearningSources] ([Id]),
                        CONSTRAINT [FK_Flashcards_WikiPages_WikiPageId] FOREIGN KEY ([WikiPageId]) REFERENCES [WikiPages] ([Id]),
                        CONSTRAINT [FK_Flashcards_QuizAttempts_QuizAttemptId] FOREIGN KEY ([QuizAttemptId]) REFERENCES [QuizAttempts] ([Id])
                    );
                END;
                IF COL_LENGTH('Flashcards', 'UserId') IS NULL ALTER TABLE Flashcards ADD UserId uniqueidentifier NULL;
                IF COL_LENGTH('Flashcards', 'TopicId') IS NULL ALTER TABLE Flashcards ADD TopicId uniqueidentifier NULL;
                IF COL_LENGTH('Flashcards', 'LearningSourceId') IS NULL ALTER TABLE Flashcards ADD LearningSourceId uniqueidentifier NULL;
                IF COL_LENGTH('Flashcards', 'WikiPageId') IS NULL ALTER TABLE Flashcards ADD WikiPageId uniqueidentifier NULL;
                IF COL_LENGTH('Flashcards', 'QuizAttemptId') IS NULL ALTER TABLE Flashcards ADD QuizAttemptId uniqueidentifier NULL;
                IF COL_LENGTH('Flashcards', 'Front') IS NULL ALTER TABLE Flashcards ADD Front nvarchar(max) NULL;
                IF COL_LENGTH('Flashcards', 'Back') IS NULL ALTER TABLE Flashcards ADD Back nvarchar(max) NULL;
                IF COL_LENGTH('Flashcards', 'Hint') IS NULL ALTER TABLE Flashcards ADD Hint nvarchar(max) NULL;
                IF COL_LENGTH('Flashcards', 'SkillTag') IS NULL ALTER TABLE Flashcards ADD SkillTag nvarchar(max) NULL;
                IF COL_LENGTH('Flashcards', 'ConceptTag') IS NULL ALTER TABLE Flashcards ADD ConceptTag nvarchar(max) NULL;
                IF COL_LENGTH('Flashcards', 'LearningObjective') IS NULL ALTER TABLE Flashcards ADD LearningObjective nvarchar(max) NULL;
                IF COL_LENGTH('Flashcards', 'Difficulty') IS NULL ALTER TABLE Flashcards ADD Difficulty nvarchar(max) NULL;
                IF COL_LENGTH('Flashcards', 'Status') IS NULL ALTER TABLE Flashcards ADD Status nvarchar(450) NOT NULL CONSTRAINT DF_Flashcards_Status DEFAULT('active');
                IF COL_LENGTH('Flashcards', 'CreatedFrom') IS NULL ALTER TABLE Flashcards ADD CreatedFrom nvarchar(max) NOT NULL CONSTRAINT DF_Flashcards_CreatedFrom DEFAULT('manual');
                IF COL_LENGTH('Flashcards', 'CreatedAt') IS NULL ALTER TABLE Flashcards ADD CreatedAt datetime2 NOT NULL CONSTRAINT DF_Flashcards_CreatedAt DEFAULT SYSUTCDATETIME();
                IF COL_LENGTH('Flashcards', 'UpdatedAt') IS NULL ALTER TABLE Flashcards ADD UpdatedAt datetime2 NOT NULL CONSTRAINT DF_Flashcards_UpdatedAt DEFAULT SYSUTCDATETIME();
                IF COL_LENGTH('Flashcards', 'LastReviewedAt') IS NULL ALTER TABLE Flashcards ADD LastReviewedAt datetime2 NULL;
                IF COL_LENGTH('Flashcards', 'MetadataJson') IS NULL ALTER TABLE Flashcards ADD MetadataJson nvarchar(max) NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Flashcards_UserId_TopicId_Status' AND object_id = OBJECT_ID('Flashcards'))
                    CREATE INDEX [IX_Flashcards_UserId_TopicId_Status] ON [Flashcards] ([UserId], [TopicId], [Status]);
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[Notifications]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [Notifications] (
                        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_Notifications] PRIMARY KEY,
                        [UserId] uniqueidentifier NOT NULL,
                        [Type] nvarchar(max) NOT NULL,
                        [Title] nvarchar(max) NOT NULL,
                        [Body] nvarchar(max) NOT NULL,
                        [Status] nvarchar(450) NOT NULL,
                        [Severity] nvarchar(max) NOT NULL,
                        [RelatedEntityType] nvarchar(max) NULL,
                        [RelatedEntityId] uniqueidentifier NULL,
                        [Channel] nvarchar(max) NOT NULL,
                        [PushStatus] nvarchar(max) NULL,
                        [FirebaseMessageId] nvarchar(max) NULL,
                        [ErrorMessage] nvarchar(max) NULL,
                        [CreatedAt] datetime2 NOT NULL,
                        [ReadAt] datetime2 NULL,
                        [ExpiresAt] datetime2 NULL,
                        [MetadataJson] nvarchar(max) NULL,
                        CONSTRAINT [FK_Notifications_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id])
                    );
                END;
                IF COL_LENGTH('Notifications', 'UserId') IS NULL ALTER TABLE Notifications ADD UserId uniqueidentifier NULL;
                IF COL_LENGTH('Notifications', 'Type') IS NULL ALTER TABLE Notifications ADD Type nvarchar(max) NOT NULL CONSTRAINT DF_Notifications_Type DEFAULT('general');
                IF COL_LENGTH('Notifications', 'Title') IS NULL ALTER TABLE Notifications ADD Title nvarchar(max) NOT NULL CONSTRAINT DF_Notifications_Title DEFAULT('Orka');
                IF COL_LENGTH('Notifications', 'Body') IS NULL ALTER TABLE Notifications ADD Body nvarchar(max) NOT NULL CONSTRAINT DF_Notifications_Body DEFAULT('');
                IF COL_LENGTH('Notifications', 'Status') IS NULL ALTER TABLE Notifications ADD Status nvarchar(450) NOT NULL CONSTRAINT DF_Notifications_Status DEFAULT('unread');
                IF COL_LENGTH('Notifications', 'Severity') IS NULL ALTER TABLE Notifications ADD Severity nvarchar(max) NOT NULL CONSTRAINT DF_Notifications_Severity DEFAULT('info');
                IF COL_LENGTH('Notifications', 'RelatedEntityType') IS NULL ALTER TABLE Notifications ADD RelatedEntityType nvarchar(max) NULL;
                IF COL_LENGTH('Notifications', 'RelatedEntityId') IS NULL ALTER TABLE Notifications ADD RelatedEntityId uniqueidentifier NULL;
                IF COL_LENGTH('Notifications', 'Channel') IS NULL ALTER TABLE Notifications ADD Channel nvarchar(max) NOT NULL CONSTRAINT DF_Notifications_Channel DEFAULT('in-app');
                IF COL_LENGTH('Notifications', 'PushStatus') IS NULL ALTER TABLE Notifications ADD PushStatus nvarchar(max) NULL;
                IF COL_LENGTH('Notifications', 'FirebaseMessageId') IS NULL ALTER TABLE Notifications ADD FirebaseMessageId nvarchar(max) NULL;
                IF COL_LENGTH('Notifications', 'ErrorMessage') IS NULL ALTER TABLE Notifications ADD ErrorMessage nvarchar(max) NULL;
                IF COL_LENGTH('Notifications', 'CreatedAt') IS NULL ALTER TABLE Notifications ADD CreatedAt datetime2 NOT NULL CONSTRAINT DF_Notifications_CreatedAt DEFAULT SYSUTCDATETIME();
                IF COL_LENGTH('Notifications', 'ReadAt') IS NULL ALTER TABLE Notifications ADD ReadAt datetime2 NULL;
                IF COL_LENGTH('Notifications', 'ExpiresAt') IS NULL ALTER TABLE Notifications ADD ExpiresAt datetime2 NULL;
                IF COL_LENGTH('Notifications', 'MetadataJson') IS NULL ALTER TABLE Notifications ADD MetadataJson nvarchar(max) NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Notifications_UserId_Status_CreatedAt' AND object_id = OBJECT_ID('Notifications'))
                    CREATE INDEX [IX_Notifications_UserId_Status_CreatedAt] ON [Notifications] ([UserId], [Status], [CreatedAt]);
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[ReviewItems]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [ReviewItems] (
                        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_ReviewItems] PRIMARY KEY,
                        [UserId] uniqueidentifier NOT NULL,
                        [TopicId] uniqueidentifier NULL,
                        [ReviewKey] nvarchar(450) NOT NULL,
                        [SkillTag] nvarchar(max) NULL,
                        [ConceptTag] nvarchar(max) NULL,
                        [LearningObjective] nvarchar(max) NULL,
                        [MistakeCategory] nvarchar(max) NULL,
                        [SourceType] nvarchar(max) NULL,
                        [SourceId] uniqueidentifier NULL,
                        [DueAt] datetime2 NOT NULL,
                        [LastReviewedAt] datetime2 NULL,
                        [IntervalDays] int NOT NULL,
                        [EaseFactor] float NOT NULL,
                        [RepetitionCount] int NOT NULL,
                        [LapseCount] int NOT NULL,
                        [SuccessStreak] int NOT NULL,
                        [Status] nvarchar(450) NOT NULL,
                        [QuizAttemptId] uniqueidentifier NULL,
                        [LearningSignalId] uniqueidentifier NULL,
                        [FlashcardId] uniqueidentifier NULL,
                        [RemediationPlanId] uniqueidentifier NULL,
                        [CreatedAt] datetime2 NOT NULL,
                        [UpdatedAt] datetime2 NOT NULL,
                        [MetadataJson] nvarchar(max) NULL,
                        CONSTRAINT [FK_ReviewItems_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]),
                        CONSTRAINT [FK_ReviewItems_Topics_TopicId] FOREIGN KEY ([TopicId]) REFERENCES [Topics] ([Id]),
                        CONSTRAINT [FK_ReviewItems_QuizAttempts_QuizAttemptId] FOREIGN KEY ([QuizAttemptId]) REFERENCES [QuizAttempts] ([Id]),
                        CONSTRAINT [FK_ReviewItems_LearningSignals_LearningSignalId] FOREIGN KEY ([LearningSignalId]) REFERENCES [LearningSignals] ([Id]),
                        CONSTRAINT [FK_ReviewItems_Flashcards_FlashcardId] FOREIGN KEY ([FlashcardId]) REFERENCES [Flashcards] ([Id]),
                        CONSTRAINT [FK_ReviewItems_RemediationPlans_RemediationPlanId] FOREIGN KEY ([RemediationPlanId]) REFERENCES [RemediationPlans] ([Id])
                    );
                END;
                IF COL_LENGTH('ReviewItems', 'UserId') IS NULL ALTER TABLE ReviewItems ADD UserId uniqueidentifier NULL;
                IF COL_LENGTH('ReviewItems', 'TopicId') IS NULL ALTER TABLE ReviewItems ADD TopicId uniqueidentifier NULL;
                IF COL_LENGTH('ReviewItems', 'ReviewKey') IS NULL ALTER TABLE ReviewItems ADD ReviewKey nvarchar(450) NOT NULL CONSTRAINT DF_ReviewItems_ReviewKey DEFAULT('topic:global:general');
                IF COL_LENGTH('ReviewItems', 'SkillTag') IS NULL ALTER TABLE ReviewItems ADD SkillTag nvarchar(max) NULL;
                IF COL_LENGTH('ReviewItems', 'ConceptTag') IS NULL ALTER TABLE ReviewItems ADD ConceptTag nvarchar(max) NULL;
                IF COL_LENGTH('ReviewItems', 'LearningObjective') IS NULL ALTER TABLE ReviewItems ADD LearningObjective nvarchar(max) NULL;
                IF COL_LENGTH('ReviewItems', 'MistakeCategory') IS NULL ALTER TABLE ReviewItems ADD MistakeCategory nvarchar(max) NULL;
                IF COL_LENGTH('ReviewItems', 'SourceType') IS NULL ALTER TABLE ReviewItems ADD SourceType nvarchar(max) NULL;
                IF COL_LENGTH('ReviewItems', 'SourceId') IS NULL ALTER TABLE ReviewItems ADD SourceId uniqueidentifier NULL;
                IF COL_LENGTH('ReviewItems', 'DueAt') IS NULL ALTER TABLE ReviewItems ADD DueAt datetime2 NOT NULL CONSTRAINT DF_ReviewItems_DueAt DEFAULT SYSUTCDATETIME();
                IF COL_LENGTH('ReviewItems', 'LastReviewedAt') IS NULL ALTER TABLE ReviewItems ADD LastReviewedAt datetime2 NULL;
                IF COL_LENGTH('ReviewItems', 'IntervalDays') IS NULL ALTER TABLE ReviewItems ADD IntervalDays int NOT NULL CONSTRAINT DF_ReviewItems_IntervalDays DEFAULT(0);
                IF COL_LENGTH('ReviewItems', 'EaseFactor') IS NULL ALTER TABLE ReviewItems ADD EaseFactor float NOT NULL CONSTRAINT DF_ReviewItems_EaseFactor DEFAULT(2.5);
                IF COL_LENGTH('ReviewItems', 'RepetitionCount') IS NULL ALTER TABLE ReviewItems ADD RepetitionCount int NOT NULL CONSTRAINT DF_ReviewItems_RepetitionCount DEFAULT(0);
                IF COL_LENGTH('ReviewItems', 'LapseCount') IS NULL ALTER TABLE ReviewItems ADD LapseCount int NOT NULL CONSTRAINT DF_ReviewItems_LapseCount DEFAULT(0);
                IF COL_LENGTH('ReviewItems', 'SuccessStreak') IS NULL ALTER TABLE ReviewItems ADD SuccessStreak int NOT NULL CONSTRAINT DF_ReviewItems_SuccessStreak DEFAULT(0);
                IF COL_LENGTH('ReviewItems', 'Status') IS NULL ALTER TABLE ReviewItems ADD Status nvarchar(450) NOT NULL CONSTRAINT DF_ReviewItems_Status DEFAULT('active');
                IF COL_LENGTH('ReviewItems', 'QuizAttemptId') IS NULL ALTER TABLE ReviewItems ADD QuizAttemptId uniqueidentifier NULL;
                IF COL_LENGTH('ReviewItems', 'LearningSignalId') IS NULL ALTER TABLE ReviewItems ADD LearningSignalId uniqueidentifier NULL;
                IF COL_LENGTH('ReviewItems', 'FlashcardId') IS NULL ALTER TABLE ReviewItems ADD FlashcardId uniqueidentifier NULL;
                IF COL_LENGTH('ReviewItems', 'RemediationPlanId') IS NULL ALTER TABLE ReviewItems ADD RemediationPlanId uniqueidentifier NULL;
                IF COL_LENGTH('ReviewItems', 'CreatedAt') IS NULL ALTER TABLE ReviewItems ADD CreatedAt datetime2 NOT NULL CONSTRAINT DF_ReviewItems_CreatedAt DEFAULT SYSUTCDATETIME();
                IF COL_LENGTH('ReviewItems', 'UpdatedAt') IS NULL ALTER TABLE ReviewItems ADD UpdatedAt datetime2 NOT NULL CONSTRAINT DF_ReviewItems_UpdatedAt DEFAULT SYSUTCDATETIME();
                IF COL_LENGTH('ReviewItems', 'MetadataJson') IS NULL ALTER TABLE ReviewItems ADD MetadataJson nvarchar(max) NULL;
                IF COL_LENGTH('ReviewItems', 'NextReviewAt') IS NOT NULL ALTER TABLE ReviewItems ALTER COLUMN NextReviewAt datetime2 NULL;
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ReviewItems_UserId_Status_DueAt' AND object_id = OBJECT_ID('ReviewItems'))
                    CREATE INDEX [IX_ReviewItems_UserId_Status_DueAt] ON [ReviewItems] ([UserId], [Status], [DueAt]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ReviewItems_UserId_TopicId' AND object_id = OBJECT_ID('ReviewItems'))
                    CREATE INDEX [IX_ReviewItems_UserId_TopicId] ON [ReviewItems] ([UserId], [TopicId]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ReviewItems_UserId_ReviewKey' AND object_id = OBJECT_ID('ReviewItems'))
                    CREATE UNIQUE INDEX [IX_ReviewItems_UserId_ReviewKey] ON [ReviewItems] ([UserId], [ReviewKey]) WHERE [Status] = 'active' AND [ReviewKey] <> 'topic:global:general';
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[UserBadges]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [UserBadges] (
                        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_UserBadges] PRIMARY KEY,
                        [UserId] uniqueidentifier NOT NULL,
                        [BadgeId] uniqueidentifier NOT NULL,
                        [EarnedAt] datetime2 NOT NULL,
                        [SourceEventId] uniqueidentifier NULL,
                        [MetadataJson] nvarchar(max) NULL,
                        CONSTRAINT [FK_UserBadges_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]),
                        CONSTRAINT [FK_UserBadges_Badges_BadgeId] FOREIGN KEY ([BadgeId]) REFERENCES [Badges] ([Id]),
                        CONSTRAINT [FK_UserBadges_XpEvents_SourceEventId] FOREIGN KEY ([SourceEventId]) REFERENCES [XpEvents] ([Id])
                    );
                END;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserBadges_UserId_BadgeId' AND object_id = OBJECT_ID('UserBadges'))
                    CREATE UNIQUE INDEX [IX_UserBadges_UserId_BadgeId] ON [UserBadges] ([UserId], [BadgeId]);
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[DailyChallenges]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [DailyChallenges] (
                        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_DailyChallenges] PRIMARY KEY,
                        [UserId] uniqueidentifier NOT NULL,
                        [TopicId] uniqueidentifier NULL,
                        [Date] datetime2 NOT NULL,
                        [SourceType] nvarchar(max) NULL,
                        [SourceSkillTag] nvarchar(max) NULL,
                        [SourceConceptTag] nvarchar(max) NULL,
                        [ReviewItemId] uniqueidentifier NULL,
                        [QuestionsJson] nvarchar(max) NOT NULL,
                        [Status] nvarchar(max) NOT NULL,
                        [Score] int NULL,
                        [CorrectCount] int NULL,
                        [CreatedAt] datetime2 NOT NULL,
                        [CompletedAt] datetime2 NULL,
                        [MetadataJson] nvarchar(max) NULL,
                        CONSTRAINT [FK_DailyChallenges_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]),
                        CONSTRAINT [FK_DailyChallenges_Topics_TopicId] FOREIGN KEY ([TopicId]) REFERENCES [Topics] ([Id]),
                        CONSTRAINT [FK_DailyChallenges_ReviewItems_ReviewItemId] FOREIGN KEY ([ReviewItemId]) REFERENCES [ReviewItems] ([Id])
                    );
                END;
                IF COL_LENGTH('DailyChallenges', 'UserId') IS NULL ALTER TABLE DailyChallenges ADD UserId uniqueidentifier NULL;
                IF COL_LENGTH('DailyChallenges', 'TopicId') IS NULL ALTER TABLE DailyChallenges ADD TopicId uniqueidentifier NULL;
                IF COL_LENGTH('DailyChallenges', 'Date') IS NULL ALTER TABLE DailyChallenges ADD [Date] datetime2 NOT NULL CONSTRAINT DF_DailyChallenges_Date DEFAULT CONVERT(date, SYSUTCDATETIME());
                IF COL_LENGTH('DailyChallenges', 'SourceType') IS NULL ALTER TABLE DailyChallenges ADD SourceType nvarchar(max) NULL;
                IF COL_LENGTH('DailyChallenges', 'SourceSkillTag') IS NULL ALTER TABLE DailyChallenges ADD SourceSkillTag nvarchar(max) NULL;
                IF COL_LENGTH('DailyChallenges', 'SourceConceptTag') IS NULL ALTER TABLE DailyChallenges ADD SourceConceptTag nvarchar(max) NULL;
                IF COL_LENGTH('DailyChallenges', 'ReviewItemId') IS NULL ALTER TABLE DailyChallenges ADD ReviewItemId uniqueidentifier NULL;
                IF COL_LENGTH('DailyChallenges', 'QuestionsJson') IS NULL ALTER TABLE DailyChallenges ADD QuestionsJson nvarchar(max) NOT NULL CONSTRAINT DF_DailyChallenges_QuestionsJson DEFAULT('[]');
                IF COL_LENGTH('DailyChallenges', 'Status') IS NULL ALTER TABLE DailyChallenges ADD Status nvarchar(max) NOT NULL CONSTRAINT DF_DailyChallenges_Status DEFAULT('active');
                IF COL_LENGTH('DailyChallenges', 'Score') IS NULL ALTER TABLE DailyChallenges ADD Score int NULL;
                IF COL_LENGTH('DailyChallenges', 'Score') IS NOT NULL ALTER TABLE DailyChallenges ALTER COLUMN Score int NULL;
                IF COL_LENGTH('DailyChallenges', 'CorrectCount') IS NULL ALTER TABLE DailyChallenges ADD CorrectCount int NULL;
                IF COL_LENGTH('DailyChallenges', 'CorrectCount') IS NOT NULL ALTER TABLE DailyChallenges ALTER COLUMN CorrectCount int NULL;
                IF COL_LENGTH('DailyChallenges', 'CreatedAt') IS NULL ALTER TABLE DailyChallenges ADD CreatedAt datetime2 NOT NULL CONSTRAINT DF_DailyChallenges_CreatedAt DEFAULT SYSUTCDATETIME();
                IF COL_LENGTH('DailyChallenges', 'CompletedAt') IS NULL ALTER TABLE DailyChallenges ADD CompletedAt datetime2 NULL;
                IF COL_LENGTH('DailyChallenges', 'MetadataJson') IS NULL ALTER TABLE DailyChallenges ADD MetadataJson nvarchar(max) NULL;
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DailyChallenges_UserId_TopicId_Date' AND object_id = OBJECT_ID('DailyChallenges'))
                    CREATE UNIQUE INDEX [IX_DailyChallenges_UserId_TopicId_Date] ON [DailyChallenges] ([UserId], [TopicId], [Date]) WHERE [TopicId] IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[DailyChallengeSubmissions]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [DailyChallengeSubmissions] (
                        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_DailyChallengeSubmissions] PRIMARY KEY,
                        [UserId] uniqueidentifier NOT NULL,
                        [DailyChallengeId] uniqueidentifier NOT NULL,
                        [Answer] nvarchar(max) NOT NULL,
                        [Quality] int NOT NULL,
                        [XpAwarded] int NOT NULL,
                        [XpEventId] uniqueidentifier NULL,
                        [CreatedAt] datetime2 NOT NULL,
                        [MetadataJson] nvarchar(max) NULL,
                        CONSTRAINT [FK_DailyChallengeSubmissions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]),
                        CONSTRAINT [FK_DailyChallengeSubmissions_DailyChallenges_DailyChallengeId] FOREIGN KEY ([DailyChallengeId]) REFERENCES [DailyChallenges] ([Id]),
                        CONSTRAINT [FK_DailyChallengeSubmissions_XpEvents_XpEventId] FOREIGN KEY ([XpEventId]) REFERENCES [XpEvents] ([Id])
                    );
                END;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DailyChallengeSubmissions_UserId_DailyChallengeId' AND object_id = OBJECT_ID('DailyChallengeSubmissions'))
                    CREATE UNIQUE INDEX [IX_DailyChallengeSubmissions_UserId_DailyChallengeId] ON [DailyChallengeSubmissions] ([UserId], [DailyChallengeId]);
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Topics_UserId_PlanIntent' AND object_id = OBJECT_ID('Topics'))
                    CREATE INDEX [IX_Topics_UserId_PlanIntent] ON [Topics] ([UserId], [PlanIntent]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SourceChunks_LearningSourceId_IsDeleted_PageNumber_ChunkIndex' AND object_id = OBJECT_ID('SourceChunks'))
                    CREATE INDEX [IX_SourceChunks_LearningSourceId_IsDeleted_PageNumber_ChunkIndex] ON [SourceChunks] ([LearningSourceId], [IsDeleted], [PageNumber], [ChunkIndex]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LearningSources_UserId_TopicId_IsDeleted' AND object_id = OBJECT_ID('LearningSources'))
                    CREATE INDEX [IX_LearningSources_UserId_TopicId_IsDeleted] ON [LearningSources] ([UserId], [TopicId], [IsDeleted]);
                """);

            migrationBuilder.Sql("""
                INSERT INTO Badges (Id, Code, Name, Description, IconKey, RuleType, Threshold, IsActive, CreatedAt, MetadataJson)
                SELECT NEWID(), v.Code, v.Name, v.Description, v.IconKey, v.RuleType, v.Threshold, 1, SYSUTCDATETIME(), NULL
                FROM (VALUES
                    ('first_review_completed', 'First Review', 'Completed a review item.', 'repeat', 'event', NULL),
                    ('daily_challenge_completed', 'Daily Spark', 'Completed a daily challenge.', 'flame', 'event', NULL),
                    ('source_learning_started', 'Source Learner', 'Started learning from a source.', 'file-text', 'event', NULL),
                    ('xp_100', '100 XP', 'Reached 100 XP.', 'award', 'threshold', 100)
                ) AS v(Code, Name, Description, IconKey, RuleType, Threshold)
                WHERE NOT EXISTS (SELECT 1 FROM Badges b WHERE b.Code = v.Code);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS [DailyChallengeSubmissions];
                DROP TABLE IF EXISTS [DailyChallenges];
                DROP TABLE IF EXISTS [UserBadges];
                DROP TABLE IF EXISTS [ReviewItems];
                DROP TABLE IF EXISTS [Notifications];
                DROP TABLE IF EXISTS [Flashcards];
                DROP TABLE IF EXISTS [Badges];
                DROP TABLE IF EXISTS [XpEvents];
                """);
        }
    }
}
