using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UniqueCreditRefundPerOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_credit_transactions_OrderId",
                table: "credit_transactions");

            migrationBuilder.CreateIndex(
                name: "IX_credit_transactions_RefundOrderId",
                table: "credit_transactions",
                column: "OrderId",
                unique: true,
                filter: "\"OrderId\" IS NOT NULL AND \"Type\" = 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_credit_transactions_RefundOrderId",
                table: "credit_transactions");

            migrationBuilder.CreateIndex(
                name: "IX_credit_transactions_OrderId",
                table: "credit_transactions",
                column: "OrderId",
                unique: true,
                filter: "\"OrderId\" IS NOT NULL AND \"Type\" = 1");
        }
    }
}
