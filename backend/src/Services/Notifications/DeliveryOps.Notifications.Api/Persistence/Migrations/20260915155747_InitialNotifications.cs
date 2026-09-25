using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Notifications.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.CreateTable(
                name: "notification_receipts",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TargetCount = table.Column<int>(type: "integer", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_receipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "push_devices",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourierId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpoPushToken = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DeviceName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_push_devices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notification_receipts_EventId",
                schema: "notifications",
                table: "notification_receipts",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_push_devices_BusinessId_BranchId_IsActive",
                schema: "notifications",
                table: "push_devices",
                columns: new[] { "BusinessId", "BranchId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_push_devices_CourierId_IsActive",
                schema: "notifications",
                table: "push_devices",
                columns: new[] { "CourierId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_push_devices_ExpoPushToken",
                schema: "notifications",
                table: "push_devices",
                column: "ExpoPushToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_receipts",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "push_devices",
                schema: "notifications");
        }
    }
}
