using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantExternalOrderUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_Source_ExternalId",
                table: "orders");

            migrationBuilder.CreateIndex(
                name: "IX_orders_BusinessId_Source_ExternalId",
                table: "orders",
                columns: new[] { "BusinessId", "Source", "ExternalId" },
                unique: true,
                filter: "\"ExternalId\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_BusinessId_Source_ExternalId",
                table: "orders");

            migrationBuilder.CreateIndex(
                name: "IX_orders_Source_ExternalId",
                table: "orders",
                columns: new[] { "Source", "ExternalId" },
                unique: true,
                filter: "\"ExternalId\" <> ''");
        }
    }
}
