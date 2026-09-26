using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryClusteringSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DeliveryClusterRadiusKm",
                table: "business_dispatch_settings",
                type: "double precision",
                nullable: false,
                defaultValue: 2.0);

            migrationBuilder.AddColumn<bool>(
                name: "PreferDeliveryClusters",
                table: "business_dispatch_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryClusterRadiusKm",
                table: "business_dispatch_settings");

            migrationBuilder.DropColumn(
                name: "PreferDeliveryClusters",
                table: "business_dispatch_settings");
        }
    }
}
