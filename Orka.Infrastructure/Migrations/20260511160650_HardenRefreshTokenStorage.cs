using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orka.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenRefreshTokenStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_Token",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens");

            migrationBuilder.AddColumn<string>(
                name: "ReplacedByTokenHash",
                table: "RefreshTokens",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevokedAt",
                table: "RefreshTokens",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevokedReason",
                table: "RefreshTokens",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "RefreshTokens",
                type: "varbinary(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TokenFamilyId",
                table: "RefreshTokens",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TokenHash",
                table: "RefreshTokens",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE [RefreshTokens]
                SET
                    [IsRevoked] = CAST(1 AS bit),
                    [RevokedAt] = COALESCE([RevokedAt], SYSUTCDATETIME()),
                    [RevokedReason] = COALESCE([RevokedReason], N'MigrationForcedLogout'),
                    [TokenFamilyId] = COALESCE([TokenFamilyId], NEWID()),
                    [TokenHash] = LOWER(CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(nvarchar(36), [Id])), 2)),
                    [RowVersion] = CRYPT_GEN_RANDOM(16)
                """);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "RefreshTokens",
                type: "varbinary(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "varbinary(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TokenFamilyId",
                table: "RefreshTokens",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TokenHash",
                table: "RefreshTokens",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "Token",
                table: "RefreshTokens");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId_TokenFamilyId",
                table: "RefreshTokens",
                columns: new[] { "UserId", "TokenFamilyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_UserId_TokenFamilyId",
                table: "RefreshTokens");

            migrationBuilder.AddColumn<string>(
                name: "Token",
                table: "RefreshTokens",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE [RefreshTokens]
                SET [Token] = CONCAT(N'rollback-token-unavailable-', CONVERT(nvarchar(36), [Id]))
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Token",
                table: "RefreshTokens",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "ReplacedByTokenHash",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "RevokedAt",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "RevokedReason",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "TokenFamilyId",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "TokenHash",
                table: "RefreshTokens");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_Token",
                table: "RefreshTokens",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens",
                column: "UserId");
        }
    }
}
