using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Auth.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefreshTokenFamilies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FamilyId",
                table: "refresh_tokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql("UPDATE refresh_tokens SET \"FamilyId\" = gen_random_uuid() WHERE \"FamilyId\" = '00000000-0000-0000-0000-000000000000';");

            migrationBuilder.AddColumn<string>(
                name: "ReplacedByTokenHash",
                table: "refresh_tokens",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "refresh_tokens",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_FamilyId_RevokedAtUtc",
                table: "refresh_tokens",
                columns: new[] { "FamilyId", "RevokedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_refresh_tokens_FamilyId_RevokedAtUtc",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "FamilyId",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "ReplacedByTokenHash",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "refresh_tokens");
        }
    }
}
