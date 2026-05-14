using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCurriculumSourceRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SourceRegistryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    SourceType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Publisher = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    LicenseStatus = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    VerificationStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OfficialClaimAllowed = table.Column<bool>(type: "bit", nullable: false),
                    SourceContentHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VerifiedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Visibility = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceRegistryItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceRegistryItems_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ContentLicenseReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceRegistryItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LicenseStatus = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ReviewStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PublishAllowed = table.Column<bool>(type: "bit", nullable: false),
                    DecisionReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentLicenseReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentLicenseReviews_SourceRegistryItems_SourceRegistryItemId",
                        column: x => x.SourceRegistryItemId,
                        principalTable: "SourceRegistryItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ContentLicenseReviews_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CurriculumVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceRegistryItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VersionLabel = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    VerificationStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OfficialClaimAllowed = table.Column<bool>(type: "bit", nullable: false),
                    SourceSnapshotHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EffectiveUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurriculumVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CurriculumVersions_ExamDefinitions_ExamDefinitionId",
                        column: x => x.ExamDefinitionId,
                        principalTable: "ExamDefinitions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CurriculumVersions_SourceRegistryItems_SourceRegistryItemId",
                        column: x => x.SourceRegistryItemId,
                        principalTable: "SourceRegistryItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CurriculumVersions_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SourceVerificationRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceRegistryItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VerificationStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    VerificationMethod = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EvidenceLocator = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    InternalNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VerifiedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceVerificationRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceVerificationRecords_SourceRegistryItems_SourceRegistryItemId",
                        column: x => x.SourceRegistryItemId,
                        principalTable: "SourceRegistryItems",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CurriculumNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurriculumVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentCurriculumNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NodeType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VerificationStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OfficialClaimAllowed = table.Column<bool>(type: "bit", nullable: false),
                    SourceAnchor = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurriculumNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CurriculumNodes_CurriculumNodes_ParentCurriculumNodeId",
                        column: x => x.ParentCurriculumNodeId,
                        principalTable: "CurriculumNodes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CurriculumNodes_CurriculumVersions_CurriculumVersionId",
                        column: x => x.CurriculumVersionId,
                        principalTable: "CurriculumVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OfficialClaimPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IsAllowed = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    SourceVerificationRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialClaimPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfficialClaimPolicies_SourceVerificationRecords_SourceVerificationRecordId",
                        column: x => x.SourceVerificationRecordId,
                        principalTable: "SourceVerificationRecords",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CurriculumOutcomeMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurriculumVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurriculumNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamOutcomeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceRegistryItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MappingType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ConfidenceStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    VerificationStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OfficialClaimAllowed = table.Column<bool>(type: "bit", nullable: false),
                    SourceLocator = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurriculumOutcomeMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CurriculumOutcomeMappings_CurriculumNodes_CurriculumNodeId",
                        column: x => x.CurriculumNodeId,
                        principalTable: "CurriculumNodes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CurriculumOutcomeMappings_CurriculumVersions_CurriculumVersionId",
                        column: x => x.CurriculumVersionId,
                        principalTable: "CurriculumVersions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CurriculumOutcomeMappings_ExamOutcomes_ExamOutcomeId",
                        column: x => x.ExamOutcomeId,
                        principalTable: "ExamOutcomes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CurriculumOutcomeMappings_SourceRegistryItems_SourceRegistryItemId",
                        column: x => x.SourceRegistryItemId,
                        principalTable: "SourceRegistryItems",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentLicenseReviews_ReviewedByUserId",
                table: "ContentLicenseReviews",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentLicenseReviews_SourceRegistryItemId_ReviewStatus_IsDeleted",
                table: "ContentLicenseReviews",
                columns: new[] { "SourceRegistryItemId", "ReviewStatus", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumNodes_CurriculumVersionId_ParentCurriculumNodeId_Code_IsDeleted",
                table: "CurriculumNodes",
                columns: new[] { "CurriculumVersionId", "ParentCurriculumNodeId", "Code", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumNodes_ParentCurriculumNodeId",
                table: "CurriculumNodes",
                column: "ParentCurriculumNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumOutcomeMappings_CurriculumNodeId",
                table: "CurriculumOutcomeMappings",
                column: "CurriculumNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumOutcomeMappings_CurriculumVersionId_CurriculumNodeId_ExamOutcomeId_IsDeleted",
                table: "CurriculumOutcomeMappings",
                columns: new[] { "CurriculumVersionId", "CurriculumNodeId", "ExamOutcomeId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumOutcomeMappings_ExamOutcomeId_VerificationStatus_IsDeleted",
                table: "CurriculumOutcomeMappings",
                columns: new[] { "ExamOutcomeId", "VerificationStatus", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumOutcomeMappings_SourceRegistryItemId",
                table: "CurriculumOutcomeMappings",
                column: "SourceRegistryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumVersions_ExamDefinitionId_OwnerUserId_Code_IsDeleted",
                table: "CurriculumVersions",
                columns: new[] { "ExamDefinitionId", "OwnerUserId", "Code", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumVersions_OwnerUserId",
                table: "CurriculumVersions",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumVersions_SourceRegistryItemId_Status_IsDeleted",
                table: "CurriculumVersions",
                columns: new[] { "SourceRegistryItemId", "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OfficialClaimPolicies_EntityType_EntityId_ClaimType_IsDeleted",
                table: "OfficialClaimPolicies",
                columns: new[] { "EntityType", "EntityId", "ClaimType", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OfficialClaimPolicies_SourceVerificationRecordId",
                table: "OfficialClaimPolicies",
                column: "SourceVerificationRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceRegistryItems_OwnerUserId_SourceKey_IsDeleted",
                table: "SourceRegistryItems",
                columns: new[] { "OwnerUserId", "SourceKey", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceRegistryItems_Visibility_VerificationStatus_IsDeleted",
                table: "SourceRegistryItems",
                columns: new[] { "Visibility", "VerificationStatus", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceVerificationRecords_SourceRegistryItemId_VerificationStatus_IsDeleted",
                table: "SourceVerificationRecords",
                columns: new[] { "SourceRegistryItemId", "VerificationStatus", "IsDeleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentLicenseReviews");

            migrationBuilder.DropTable(
                name: "CurriculumOutcomeMappings");

            migrationBuilder.DropTable(
                name: "OfficialClaimPolicies");

            migrationBuilder.DropTable(
                name: "CurriculumNodes");

            migrationBuilder.DropTable(
                name: "SourceVerificationRecords");

            migrationBuilder.DropTable(
                name: "CurriculumVersions");

            migrationBuilder.DropTable(
                name: "SourceRegistryItems");
        }
    }
}
