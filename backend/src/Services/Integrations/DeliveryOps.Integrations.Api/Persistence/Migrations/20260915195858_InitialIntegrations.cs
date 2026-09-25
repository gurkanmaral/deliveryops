using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Integrations.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "integrations");

            migrationBuilder.CreateTable(
                name: "connections",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "inbound_order_events",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalEventId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    RawPayload = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CoreOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbound_order_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inbound_order_events_connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "integrations",
                        principalTable: "connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_connections_BusinessId_BranchId_Provider",
                schema: "integrations",
                table: "connections",
                columns: new[] { "BusinessId", "BranchId", "Provider" });

            migrationBuilder.CreateIndex(
                name: "IX_inbound_order_events_ConnectionId_ExternalEventId",
                schema: "integrations",
                table: "inbound_order_events",
                columns: new[] { "ConnectionId", "ExternalEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inbound_order_events_Status_ReceivedAtUtc",
                schema: "integrations",
                table: "inbound_order_events",
                columns: new[] { "Status", "ReceivedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbound_order_events",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "connections",
                schema: "integrations");
        }
    }
}
