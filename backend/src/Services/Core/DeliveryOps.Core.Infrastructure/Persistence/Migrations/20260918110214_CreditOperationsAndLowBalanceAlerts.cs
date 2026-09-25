using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreditOperationsAndLowBalanceAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_credit_transactions_OrderId",
                table: "credit_transactions");

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "credit_transactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LowBalanceThreshold",
                table: "business_credit_accounts",
                type: "integer",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.CreateIndex(
                name: "IX_credit_transactions_BusinessId_IdempotencyKey",
                table: "credit_transactions",
                columns: new[] { "BusinessId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_credit_transactions_OrderId",
                table: "credit_transactions",
                column: "OrderId",
                unique: true,
                filter: "\"OrderId\" IS NOT NULL AND \"Type\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_credit_transactions_BusinessId_IdempotencyKey",
                table: "credit_transactions");

            migrationBuilder.DropIndex(
                name: "IX_credit_transactions_OrderId",
                table: "credit_transactions");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "credit_transactions");

            migrationBuilder.DropColumn(
                name: "LowBalanceThreshold",
                table: "business_credit_accounts");

            migrationBuilder.CreateIndex(
                name: "IX_credit_transactions_OrderId",
                table: "credit_transactions",
                column: "OrderId",
                unique: true,
                filter: "\"OrderId\" IS NOT NULL");
        }
    }
}
