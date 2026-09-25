using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Integrations.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProviderConnectionHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastHealthCheckAtUtc",
                schema: "integrations",
                table: "connections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastHealthCheckMessage",
                schema: "integrations",
                table: "connections",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LastHealthCheckSucceeded",
                schema: "integrations",
                table: "connections",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastTokenExpiresAtUtc",
                schema: "integrations",
                table: "connections",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastHealthCheckAtUtc",
                schema: "integrations",
                table: "connections");

            migrationBuilder.DropColumn(
                name: "LastHealthCheckMessage",
                schema: "integrations",
                table: "connections");

            migrationBuilder.DropColumn(
                name: "LastHealthCheckSucceeded",
                schema: "integrations",
                table: "connections");

            migrationBuilder.DropColumn(
                name: "LastTokenExpiresAtUtc",
                schema: "integrations",
                table: "connections");
        }
    }
}
