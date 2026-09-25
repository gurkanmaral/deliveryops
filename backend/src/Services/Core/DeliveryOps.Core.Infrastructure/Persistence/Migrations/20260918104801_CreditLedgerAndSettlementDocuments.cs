using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreditLedgerAndSettlementDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentNumber",
                table: "billing_settlements",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "business_credit_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Balance = table.Column<int>(type: "integer", nullable: false),
                    LifetimeAdded = table.Column<long>(type: "bigint", nullable: false),
                    LifetimeConsumed = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_credit_accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_business_credit_accounts_businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "credit_transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    BalanceAfter = table.Column<int>(type: "integer", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_credit_transactions_businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_credit_transactions_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                UPDATE billing_settlements
                SET "DocumentNumber" = 'MUT-' || TO_CHAR("PeriodTo", 'YYYYMM') || '-' ||
                    UPPER(SUBSTRING(REPLACE("Id"::text, '-', ''), 1, 8))
                WHERE "Status" = 1 AND "DocumentNumber" = '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_billing_settlements_DocumentNumber",
                table: "billing_settlements",
                column: "DocumentNumber",
                unique: true,
                filter: "\"DocumentNumber\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_business_credit_accounts_BusinessId",
                table: "business_credit_accounts",
                column: "BusinessId",
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO business_credit_accounts ("Id", "BusinessId", "Balance", "LifetimeAdded", "LifetimeConsumed", "CreatedAtUtc")
                SELECT "Id", "Id", 0, 0, 0, NOW()
                FROM businesses
                ON CONFLICT ("BusinessId") DO NOTHING;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_credit_transactions_BusinessId_CreatedAtUtc",
                table: "credit_transactions",
                columns: new[] { "BusinessId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_credit_transactions_OrderId",
                table: "credit_transactions",
                column: "OrderId",
                unique: true,
                filter: "\"OrderId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_credit_accounts");

            migrationBuilder.DropTable(
                name: "credit_transactions");

            migrationBuilder.DropIndex(
                name: "IX_billing_settlements_DocumentNumber",
                table: "billing_settlements");

            migrationBuilder.DropColumn(
                name: "DocumentNumber",
                table: "billing_settlements");
        }
    }
}
