using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Notifications.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpoPushReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "expo_push_receipts",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExpoPushToken = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextCheckAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expo_push_receipts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_expo_push_receipts_CompletedAtUtc_NextCheckAtUtc",
                schema: "notifications",
                table: "expo_push_receipts",
                columns: new[] { "CompletedAtUtc", "NextCheckAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_expo_push_receipts_TicketId",
                schema: "notifications",
                table: "expo_push_receipts",
                column: "TicketId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expo_push_receipts",
                schema: "notifications");
        }
    }
}
