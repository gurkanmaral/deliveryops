using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Integrations.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WebhookSecretRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreviousProtectedSecret",
                schema: "integrations",
                table: "connections",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousSecretHash",
                schema: "integrations",
                table: "connections",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PreviousSecretValidUntilUtc",
                schema: "integrations",
                table: "connections",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreviousProtectedSecret",
                schema: "integrations",
                table: "connections");

            migrationBuilder.DropColumn(
                name: "PreviousSecretHash",
                schema: "integrations",
                table: "connections");

            migrationBuilder.DropColumn(
                name: "PreviousSecretValidUntilUtc",
                schema: "integrations",
                table: "connections");
        }
    }
}
