using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSkillTreeFaz4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SkillNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NodeType = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IsUnlocked = table.Column<bool>(type: "bit", nullable: false),
                    DifficultyLevel = table.Column<int>(type: "int", nullable: false),
                    RuleMetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RelatedTopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UnlockedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkillNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SkillNodes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SkillTreeClosures",
                columns: table => new
                {
                    AncestorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DescendantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Depth = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkillTreeClosures", x => new { x.AncestorId, x.DescendantId });
                    table.ForeignKey(
                        name: "FK_SkillTreeClosures_SkillNodes_AncestorId",
                        column: x => x.AncestorId,
                        principalTable: "SkillNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SkillTreeClosures_SkillNodes_DescendantId",
                        column: x => x.DescendantId,
                        principalTable: "SkillNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SkillNodes_UserId",
                table: "SkillNodes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SkillNodes_UserId_NodeType",
                table: "SkillNodes",
                columns: new[] { "UserId", "NodeType" });

            migrationBuilder.CreateIndex(
                name: "IX_SkillTreeClosures_AncestorId",
                table: "SkillTreeClosures",
                column: "AncestorId");

            migrationBuilder.CreateIndex(
                name: "IX_SkillTreeClosures_DescendantId",
                table: "SkillTreeClosures",
                column: "DescendantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SkillTreeClosures");

            migrationBuilder.DropTable(
                name: "SkillNodes");
        }
    }
}
