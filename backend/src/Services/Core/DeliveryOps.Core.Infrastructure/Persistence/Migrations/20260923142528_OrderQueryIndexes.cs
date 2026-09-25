using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_orders_BusinessId_BranchId_CreatedAtUtc",
                table: "orders",
                columns: new[] { "BusinessId", "BranchId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_BusinessId_CourierId_Status",
                table: "orders",
                columns: new[] { "BusinessId", "CourierId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_BusinessId_CreatedAtUtc",
                table: "orders",
                columns: new[] { "BusinessId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_BusinessId_BranchId_CreatedAtUtc",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_BusinessId_CourierId_Status",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_BusinessId_CreatedAtUtc",
                table: "orders");
        }
    }
}
