using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Integrations.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboundProviderStatusSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProtectedCredentials",
                schema: "integrations",
                table: "connections",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderAccountId",
                schema: "integrations",
                table: "connections",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProviderEnvironment",
                schema: "integrations",
                table: "connections",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "outbound_order_events",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CoreOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalOrderId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ProviderStatus = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbound_order_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_outbound_order_events_connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "integrations",
                        principalTable: "connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_outbound_order_events_ConnectionId_ExternalOrderId_Provider~",
                schema: "integrations",
                table: "outbound_order_events",
                columns: new[] { "ConnectionId", "ExternalOrderId", "ProviderStatus" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbound_order_events_SourceEventId",
                schema: "integrations",
                table: "outbound_order_events",
                column: "SourceEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbound_order_events_Status_NextAttemptAtUtc",
                schema: "integrations",
                table: "outbound_order_events",
                columns: new[] { "Status", "NextAttemptAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbound_order_events",
                schema: "integrations");

            migrationBuilder.DropColumn(
                name: "ProtectedCredentials",
                schema: "integrations",
                table: "connections");

            migrationBuilder.DropColumn(
                name: "ProviderAccountId",
                schema: "integrations",
                table: "connections");

            migrationBuilder.DropColumn(
                name: "ProviderEnvironment",
                schema: "integrations",
                table: "connections");
        }
    }
}
