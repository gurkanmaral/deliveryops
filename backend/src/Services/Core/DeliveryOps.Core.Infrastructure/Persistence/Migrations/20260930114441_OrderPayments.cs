using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DeliveredDistanceMeters",
                table: "orders",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PaidAmount",
                table: "orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaidAtUtc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentChannel",
                table: "orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentCollectedByCourierId",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentMethod",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PaymentReference",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentStatus",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_orders_PaymentCollectedByCourierId_PaidAtUtc",
                table: "orders",
                columns: new[] { "PaymentCollectedByCourierId", "PaidAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_PaymentCollectedByCourierId_PaidAtUtc",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DeliveredDistanceMeters",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PaidAmount",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PaidAtUtc",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PaymentChannel",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PaymentCollectedByCourierId",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PaymentReference",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "orders");
        }
    }
}
