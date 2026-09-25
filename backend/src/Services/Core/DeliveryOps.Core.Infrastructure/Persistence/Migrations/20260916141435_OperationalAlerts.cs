using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperationalAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "business_sla_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourierWaitingWarningMinutes = table.Column<int>(type: "integer", nullable: false),
                    CourierWaitingCriticalMinutes = table.Column<int>(type: "integer", nullable: false),
                    PickupWarningMinutes = table.Column<int>(type: "integer", nullable: false),
                    PickupCriticalMinutes = table.Column<int>(type: "integer", nullable: false),
                    DeliveryWarningMinutes = table.Column<int>(type: "integer", nullable: false),
                    DeliveryCriticalMinutes = table.Column<int>(type: "integer", nullable: false),
                    LocationStaleWarningMinutes = table.Column<int>(type: "integer", nullable: false),
                    LocationStaleCriticalMinutes = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_sla_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_business_sla_settings_businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "operational_alerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    CourierId = table.Column<Guid>(type: "uuid", nullable: true),
                    AlertKey = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FirstDetectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcknowledgedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcknowledgedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operational_alerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_operational_alerts_businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_operational_alerts_couriers_CourierId",
                        column: x => x.CourierId,
                        principalTable: "couriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_operational_alerts_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_business_sla_settings_BusinessId",
                table: "business_sla_settings",
                column: "BusinessId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operational_alerts_AlertKey",
                table: "operational_alerts",
                column: "AlertKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operational_alerts_BusinessId_Status_Severity",
                table: "operational_alerts",
                columns: new[] { "BusinessId", "Status", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_operational_alerts_CourierId",
                table: "operational_alerts",
                column: "CourierId");

            migrationBuilder.CreateIndex(
                name: "IX_operational_alerts_OrderId",
                table: "operational_alerts",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_sla_settings");

            migrationBuilder.DropTable(
                name: "operational_alerts");
        }
    }
}
