using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExamFramework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExamDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExamFamily = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Visibility = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    VerificationStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OfficialClaimAllowed = table.Column<bool>(type: "bit", nullable: false),
                    SourceTitle = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    SourceUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VerifiedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamDefinitions_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ExamContentPacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ImportedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Visibility = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceOrigin = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LicenseStatus = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    VerificationStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OfficialClaimAllowed = table.Column<bool>(type: "bit", nullable: false),
                    SourceTitle = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    SourceUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamContentPacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamContentPacks_ExamDefinitions_ExamDefinitionId",
                        column: x => x.ExamDefinitionId,
                        principalTable: "ExamDefinitions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ExamContentPacks_Users_ImportedByUserId",
                        column: x => x.ImportedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ExamContentPacks_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ExamVariants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamVariants_ExamDefinitions_ExamDefinitionId",
                        column: x => x.ExamDefinitionId,
                        principalTable: "ExamDefinitions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ExamSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamVariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamSections_ExamVariants_ExamVariantId",
                        column: x => x.ExamVariantId,
                        principalTable: "ExamVariants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ExamScoringRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamVariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamSectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RuleType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamScoringRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamScoringRules_ExamSections_ExamSectionId",
                        column: x => x.ExamSectionId,
                        principalTable: "ExamSections",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ExamScoringRules_ExamVariants_ExamVariantId",
                        column: x => x.ExamVariantId,
                        principalTable: "ExamVariants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ExamSubjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamSectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamSubjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamSubjects_ExamSections_ExamSectionId",
                        column: x => x.ExamSectionId,
                        principalTable: "ExamSections",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ExamTimeRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamVariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamSectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RuleType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamTimeRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamTimeRules_ExamSections_ExamSectionId",
                        column: x => x.ExamSectionId,
                        principalTable: "ExamSections",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ExamTimeRules_ExamVariants_ExamVariantId",
                        column: x => x.ExamVariantId,
                        principalTable: "ExamVariants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ExamTopics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamSubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentExamTopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamTopics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamTopics_ExamSubjects_ExamSubjectId",
                        column: x => x.ExamSubjectId,
                        principalTable: "ExamSubjects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ExamTopics_ExamTopics_ParentExamTopicId",
                        column: x => x.ParentExamTopicId,
                        principalTable: "ExamTopics",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ExamOutcomes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamTopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamOutcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamOutcomes_ExamTopics_ExamTopicId",
                        column: x => x.ExamTopicId,
                        principalTable: "ExamTopics",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExamContentPacks_ExamDefinitionId_OwnerUserId_Code_IsDeleted",
                table: "ExamContentPacks",
                columns: new[] { "ExamDefinitionId", "OwnerUserId", "Code", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamContentPacks_ImportedByUserId_CreatedAt",
                table: "ExamContentPacks",
                columns: new[] { "ImportedByUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamContentPacks_OwnerUserId",
                table: "ExamContentPacks",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamDefinitions_Code_Visibility_IsDeleted",
                table: "ExamDefinitions",
                columns: new[] { "Code", "Visibility", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamDefinitions_OwnerUserId_Code_IsDeleted",
                table: "ExamDefinitions",
                columns: new[] { "OwnerUserId", "Code", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamOutcomes_ExamTopicId_Code_IsDeleted",
                table: "ExamOutcomes",
                columns: new[] { "ExamTopicId", "Code", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamScoringRules_ExamSectionId",
                table: "ExamScoringRules",
                column: "ExamSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamScoringRules_ExamVariantId_ExamSectionId_RuleType_IsDeleted",
                table: "ExamScoringRules",
                columns: new[] { "ExamVariantId", "ExamSectionId", "RuleType", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamSections_ExamVariantId_Code_IsDeleted",
                table: "ExamSections",
                columns: new[] { "ExamVariantId", "Code", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamSubjects_ExamSectionId_Code_IsDeleted",
                table: "ExamSubjects",
                columns: new[] { "ExamSectionId", "Code", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamTimeRules_ExamSectionId",
                table: "ExamTimeRules",
                column: "ExamSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamTimeRules_ExamVariantId_ExamSectionId_RuleType_IsDeleted",
                table: "ExamTimeRules",
                columns: new[] { "ExamVariantId", "ExamSectionId", "RuleType", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamTopics_ExamSubjectId_ParentExamTopicId_Code_IsDeleted",
                table: "ExamTopics",
                columns: new[] { "ExamSubjectId", "ParentExamTopicId", "Code", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamTopics_ParentExamTopicId",
                table: "ExamTopics",
                column: "ParentExamTopicId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamVariants_ExamDefinitionId_Code_IsDeleted",
                table: "ExamVariants",
                columns: new[] { "ExamDefinitionId", "Code", "IsDeleted" });

            migrationBuilder.Sql("""
                IF NOT EXISTS (
                    SELECT 1 FROM [ExamDefinitions]
                    WHERE [Code] = N'KPSS' AND [OwnerUserId] IS NULL AND [IsDeleted] = 0
                )
                BEGIN
                    DECLARE @now datetime2 = SYSUTCDATETIME();
                    DECLARE @definitionId uniqueidentifier = NEWID();
                    DECLARE @packId uniqueidentifier = NEWID();
                    DECLARE @lisansId uniqueidentifier = NEWID();
                    DECLARE @onlisansId uniqueidentifier = NEWID();
                    DECLARE @unverifiedLabel nvarchar(max) = N'Resmi müfredat iddiası değildir; doğrulanmış kaynak eklendiğinde resmi kaynak etiketi gösterilir.';

                    INSERT INTO [ExamDefinitions]
                        ([Id], [OwnerUserId], [Code], [Name], [Description], [ExamFamily], [Visibility],
                         [VerificationStatus], [OfficialClaimAllowed], [SourceTitle], [SourceUrl],
                         [VerifiedAt], [VerifiedBy], [CreatedAt], [UpdatedAt], [IsDeleted])
                    VALUES
                        (@definitionId, NULL, N'KPSS', N'KPSS hazırlık iskeleti', @unverifiedLabel, N'exam', N'system',
                         N'unverified', 0, NULL, NULL, NULL, NULL, @now, @now, 0);

                    INSERT INTO [ExamContentPacks]
                        ([Id], [ExamDefinitionId], [OwnerUserId], [ImportedByUserId], [Code], [Name], [Description],
                         [Visibility], [SourceOrigin], [LicenseStatus], [VerificationStatus], [OfficialClaimAllowed],
                         [SourceTitle], [SourceUrl], [Status], [PublishedAt], [CreatedAt], [UpdatedAt], [IsDeleted])
                    VALUES
                        (@packId, @definitionId, NULL, NULL, N'KPSS_UNVERIFIED_SKELETON', N'KPSS hazırlık iskeleti', @unverifiedLabel,
                         N'system', N'architecture_skeleton', N'unknown', N'unverified', 0,
                         NULL, NULL, N'published', @now, @now, @now, 0);

                    INSERT INTO [ExamVariants]
                        ([Id], [ExamDefinitionId], [Code], [Name], [Description], [SortOrder], [CreatedAt], [UpdatedAt], [IsDeleted])
                    VALUES
                        (@lisansId, @definitionId, N'KPSS_LISANS', N'KPSS Lisans', @unverifiedLabel, 0, @now, @now, 0),
                        (@onlisansId, @definitionId, N'KPSS_ONLISANS', N'KPSS Önlisans', @unverifiedLabel, 1, @now, @now, 0);

                    DECLARE variant_cursor CURSOR LOCAL FAST_FORWARD FOR
                        SELECT [Id] FROM [ExamVariants] WHERE [ExamDefinitionId] = @definitionId;

                    DECLARE @variantId uniqueidentifier;
                    OPEN variant_cursor;
                    FETCH NEXT FROM variant_cursor INTO @variantId;
                    WHILE @@FETCH_STATUS = 0
                    BEGIN
                        DECLARE @gyId uniqueidentifier = NEWID();
                        DECLARE @gkId uniqueidentifier = NEWID();

                        INSERT INTO [ExamSections]
                            ([Id], [ExamVariantId], [Code], [Name], [Description], [SortOrder], [CreatedAt], [UpdatedAt], [IsDeleted])
                        VALUES
                            (@gyId, @variantId, N'GENEL_YETENEK', N'Genel Yetenek', N'Temsilî hazırlık bölümü; resmi müfredat iddiası değildir.', 0, @now, @now, 0),
                            (@gkId, @variantId, N'GENEL_KULTUR', N'Genel Kültür', N'Temsilî hazırlık bölümü; resmi müfredat iddiası değildir.', 1, @now, @now, 0);

                        DECLARE @turkceId uniqueidentifier = NEWID();
                        DECLARE @matematikId uniqueidentifier = NEWID();
                        DECLARE @tarihId uniqueidentifier = NEWID();
                        DECLARE @cografyaId uniqueidentifier = NEWID();
                        DECLARE @vatandaslikId uniqueidentifier = NEWID();

                        INSERT INTO [ExamSubjects]
                            ([Id], [ExamSectionId], [Code], [Name], [Description], [SortOrder], [CreatedAt], [UpdatedAt], [IsDeleted])
                        VALUES
                            (@turkceId, @gyId, N'TURKCE', N'Türkçe', N'Temsilî başlık; resmi müfredat iddiası değildir.', 0, @now, @now, 0),
                            (@matematikId, @gyId, N'MATEMATIK', N'Matematik', N'Temsilî başlık; resmi müfredat iddiası değildir.', 1, @now, @now, 0),
                            (@tarihId, @gkId, N'TARIH', N'Tarih', N'Temsilî başlık; resmi müfredat iddiası değildir.', 0, @now, @now, 0),
                            (@cografyaId, @gkId, N'COGRAFYA', N'Coğrafya', N'Temsilî başlık; resmi müfredat iddiası değildir.', 1, @now, @now, 0),
                            (@vatandaslikId, @gkId, N'VATANDASLIK', N'Vatandaşlık', N'Temsilî başlık; resmi müfredat iddiası değildir.', 2, @now, @now, 0);

                        DECLARE @paragrafId uniqueidentifier = NEWID();
                        DECLARE @temelKavramlarId uniqueidentifier = NEWID();
                        DECLARE @tarihTekrarId uniqueidentifier = NEWID();
                        DECLARE @cografyaTekrarId uniqueidentifier = NEWID();
                        DECLARE @vatandaslikTopicId uniqueidentifier = NEWID();

                        INSERT INTO [ExamTopics]
                            ([Id], [ExamSubjectId], [ParentExamTopicId], [Code], [Name], [Description], [SortOrder], [CreatedAt], [UpdatedAt], [IsDeleted])
                        VALUES
                            (@paragrafId, @turkceId, NULL, N'PARAGRAF', N'Paragraf ve anlam', N'Hazırlık iskeleti için temsilî konu.', 0, @now, @now, 0),
                            (@temelKavramlarId, @matematikId, NULL, N'TEMEL_KAVRAMLAR', N'Temel kavramlar', N'Hazırlık iskeleti için temsilî konu.', 0, @now, @now, 0),
                            (@tarihTekrarId, @tarihId, NULL, N'GENEL_TARIH_TEKRAR', N'Genel tarih tekrarı', N'Hazırlık iskeleti için temsilî konu.', 0, @now, @now, 0),
                            (@cografyaTekrarId, @cografyaId, NULL, N'TURKIYE_COGRAFYASI_TEKRAR', N'Türkiye coğrafyası tekrarı', N'Hazırlık iskeleti için temsilî konu.', 0, @now, @now, 0),
                            (@vatandaslikTopicId, @vatandaslikId, NULL, N'TEMEL_VATANDASLIK', N'Temel vatandaşlık', N'Hazırlık iskeleti için temsilî konu.', 0, @now, @now, 0);

                        INSERT INTO [ExamOutcomes]
                            ([Id], [ExamTopicId], [Code], [Name], [Description], [SortOrder], [CreatedAt], [UpdatedAt], [IsDeleted])
                        VALUES
                            (NEWID(), @paragrafId, N'PARAGRAF_OUTCOME', N'Ana fikir ve çıkarım pratiği', N'Temsilî kazanım; resmi kapsam iddiası değildir.', 0, @now, @now, 0),
                            (NEWID(), @temelKavramlarId, N'TEMEL_KAVRAMLAR_OUTCOME', N'Temel işlem ve problem çözme pratiği', N'Temsilî kazanım; resmi kapsam iddiası değildir.', 0, @now, @now, 0),
                            (NEWID(), @tarihTekrarId, N'GENEL_TARIH_TEKRAR_OUTCOME', N'Kronoloji ve kavram tekrarı', N'Temsilî kazanım; resmi kapsam iddiası değildir.', 0, @now, @now, 0),
                            (NEWID(), @cografyaTekrarId, N'TURKIYE_COGRAFYASI_TEKRAR_OUTCOME', N'Harita ve bölge bilgisi tekrarı', N'Temsilî kazanım; resmi kapsam iddiası değildir.', 0, @now, @now, 0),
                            (NEWID(), @vatandaslikTopicId, N'TEMEL_VATANDASLIK_OUTCOME', N'Temel yurttaşlık kavramları', N'Temsilî kazanım; resmi kapsam iddiası değildir.', 0, @now, @now, 0);

                        FETCH NEXT FROM variant_cursor INTO @variantId;
                    END
                    CLOSE variant_cursor;
                    DEALLOCATE variant_cursor;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExamContentPacks");

            migrationBuilder.DropTable(
                name: "ExamOutcomes");

            migrationBuilder.DropTable(
                name: "ExamScoringRules");

            migrationBuilder.DropTable(
                name: "ExamTimeRules");

            migrationBuilder.DropTable(
                name: "ExamTopics");

            migrationBuilder.DropTable(
                name: "ExamSubjects");

            migrationBuilder.DropTable(
                name: "ExamSections");

            migrationBuilder.DropTable(
                name: "ExamVariants");

            migrationBuilder.DropTable(
                name: "ExamDefinitions");
        }
    }
}
