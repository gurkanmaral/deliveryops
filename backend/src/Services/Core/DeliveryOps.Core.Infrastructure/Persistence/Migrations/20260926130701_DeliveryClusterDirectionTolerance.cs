using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryClusterDirectionTolerance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DeliveryClusterMaxBearingDegrees",
                table: "business_dispatch_settings",
                type: "double precision",
                nullable: false,
                defaultValue: 45.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryClusterMaxBearingDegrees",
                table: "business_dispatch_settings");
        }
    }
}
