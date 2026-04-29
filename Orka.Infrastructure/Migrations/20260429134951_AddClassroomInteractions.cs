using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClassroomInteractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClassroomInteractions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClassroomSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Question = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AnswerScript = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AudioBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassroomInteractions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassroomInteractions_ClassroomSessions_ClassroomSessionId",
                        column: x => x.ClassroomSessionId,
                        principalTable: "ClassroomSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClassroomInteractions_ClassroomSessionId",
                table: "ClassroomInteractions",
                column: "ClassroomSessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClassroomInteractions");
        }
    }
}
