using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AutomaticDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "couriers",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "business_dispatch_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    AutoConfirmOrders = table.Column<bool>(type: "boolean", nullable: false),
                    AutoAssignCouriers = table.Column<bool>(type: "boolean", nullable: false),
                    AllowCourierSelfClaim = table.Column<bool>(type: "boolean", nullable: false),
                    PreferBranchCouriers = table.Column<bool>(type: "boolean", nullable: false),
                    MaxActiveOrdersPerCourier = table.Column<int>(type: "integer", nullable: false),
                    RequireFreshLocation = table.Column<bool>(type: "boolean", nullable: false),
                    LocationFreshnessMinutes = table.Column<int>(type: "integer", nullable: false),
                    AssignmentRadiusKm = table.Column<double>(type: "double precision", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_dispatch_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_business_dispatch_settings_businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_dispatch_states",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AssignedCourierId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_dispatch_states", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_dispatch_states_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_business_dispatch_settings_BusinessId",
                table: "business_dispatch_settings",
                column: "BusinessId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_dispatch_states_BusinessId_Status_NextAttemptAtUtc",
                table: "order_dispatch_states",
                columns: new[] { "BusinessId", "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_order_dispatch_states_OrderId",
                table: "order_dispatch_states",
                column: "OrderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_dispatch_settings");

            migrationBuilder.DropTable(
                name: "order_dispatch_states");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "couriers");
        }
    }
}
