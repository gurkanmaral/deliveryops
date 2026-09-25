using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Integrations.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProviderAdapterPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdapterVersion",
                schema: "integrations",
                table: "inbound_order_events",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "canonical-v1");

            migrationBuilder.AddColumn<string>(
                name: "EventType",
                schema: "integrations",
                table: "inbound_order_events",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "order.created");

            migrationBuilder.AddColumn<string>(
                name: "ExternalOrderId",
                schema: "integrations",
                table: "inbound_order_events",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAtUtc",
                schema: "integrations",
                table: "inbound_order_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedPayload",
                schema: "integrations",
                table: "inbound_order_events",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "PayloadHash",
                schema: "integrations",
                table: "inbound_order_events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AdapterVersion",
                schema: "integrations",
                table: "connections",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "canonical-v1");

            migrationBuilder.AddColumn<int>(
                name: "AuthMode",
                schema: "integrations",
                table: "connections",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ProtectedSecret",
                schema: "integrations",
                table: "connections",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE integrations.inbound_order_events
                SET "ExternalOrderId" = "ExternalEventId",
                    "NormalizedPayload" = "RawPayload",
                    "PayloadHash" = encode(sha256(convert_to("RawPayload"::text, 'UTF8')), 'hex'),
                    "NextAttemptAtUtc" = CASE WHEN "Status" IN (0, 3) THEN NOW() ELSE NULL END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_inbound_order_events_Status_NextAttemptAtUtc",
                schema: "integrations",
                table: "inbound_order_events",
                columns: new[] { "Status", "NextAttemptAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_inbound_order_events_Status_NextAttemptAtUtc",
                schema: "integrations",
                table: "inbound_order_events");

            migrationBuilder.DropColumn(
                name: "AdapterVersion",
                schema: "integrations",
                table: "inbound_order_events");

            migrationBuilder.DropColumn(
                name: "EventType",
                schema: "integrations",
                table: "inbound_order_events");

            migrationBuilder.DropColumn(
                name: "ExternalOrderId",
                schema: "integrations",
                table: "inbound_order_events");

            migrationBuilder.DropColumn(
                name: "NextAttemptAtUtc",
                schema: "integrations",
                table: "inbound_order_events");

            migrationBuilder.DropColumn(
                name: "NormalizedPayload",
                schema: "integrations",
                table: "inbound_order_events");

            migrationBuilder.DropColumn(
                name: "PayloadHash",
                schema: "integrations",
                table: "inbound_order_events");

            migrationBuilder.DropColumn(
                name: "AdapterVersion",
                schema: "integrations",
                table: "connections");

            migrationBuilder.DropColumn(
                name: "AuthMode",
                schema: "integrations",
                table: "connections");

            migrationBuilder.DropColumn(
                name: "ProtectedSecret",
                schema: "integrations",
                table: "connections");
        }
    }
}
