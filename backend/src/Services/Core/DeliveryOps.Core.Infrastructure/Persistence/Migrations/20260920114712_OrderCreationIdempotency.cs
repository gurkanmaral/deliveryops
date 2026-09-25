using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderCreationIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreationIdempotencyKey",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CreationRequestHash",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_orders_BusinessId_CreationIdempotencyKey",
                table: "orders",
                columns: new[] { "BusinessId", "CreationIdempotencyKey" },
                unique: true,
                filter: "\"CreationIdempotencyKey\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_BusinessId_CreationIdempotencyKey",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "CreationIdempotencyKey",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "CreationRequestHash",
                table: "orders");
        }
    }
}
