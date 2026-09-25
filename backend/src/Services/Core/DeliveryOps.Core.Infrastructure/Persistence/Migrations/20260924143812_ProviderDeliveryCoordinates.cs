using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProviderDeliveryCoordinates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeliveryFulfillment",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryInstructions",
                table: "orders",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DeliveryLatitude",
                table: "orders",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryLocationAccuracy",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryLocationSource",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "DeliveryLongitude",
                table: "orders",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_BusinessId_DeliveryLatitude_DeliveryLongitude",
                table: "orders",
                columns: new[] { "BusinessId", "DeliveryLatitude", "DeliveryLongitude" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_BusinessId_DeliveryLatitude_DeliveryLongitude",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DeliveryFulfillment",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DeliveryInstructions",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DeliveryLatitude",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DeliveryLocationAccuracy",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DeliveryLocationSource",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DeliveryLongitude",
                table: "orders");
        }
    }
}
