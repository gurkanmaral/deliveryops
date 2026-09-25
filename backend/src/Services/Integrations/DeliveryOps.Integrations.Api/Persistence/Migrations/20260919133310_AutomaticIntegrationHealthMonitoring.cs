using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Integrations.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AutomaticIntegrationHealthMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveHealthCheckFailures",
                schema: "integrations",
                table: "connections",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastAutomaticHealthCheckAtUtc",
                schema: "integrations",
                table: "connections",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConsecutiveHealthCheckFailures",
                schema: "integrations",
                table: "connections");

            migrationBuilder.DropColumn(
                name: "LastAutomaticHealthCheckAtUtc",
                schema: "integrations",
                table: "connections");
        }
    }
}
