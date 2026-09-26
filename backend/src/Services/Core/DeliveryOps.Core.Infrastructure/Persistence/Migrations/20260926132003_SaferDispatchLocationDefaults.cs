using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SaferDispatchLocationDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "RequireFreshLocation",
                table: "business_dispatch_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<double>(
                name: "AssignmentRadiusKm",
                table: "business_dispatch_settings",
                type: "double precision",
                nullable: true,
                defaultValue: 10.0,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);

            migrationBuilder.Sql("""
                UPDATE business_dispatch_settings
                SET "RequireFreshLocation" = TRUE
                WHERE "RequireFreshLocation" = FALSE;

                UPDATE business_dispatch_settings
                SET "AssignmentRadiusKm" = 10.0
                WHERE "AssignmentRadiusKm" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "RequireFreshLocation",
                table: "business_dispatch_settings",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<double>(
                name: "AssignmentRadiusKm",
                table: "business_dispatch_settings",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true,
                oldDefaultValue: 10.0);
        }
    }
}
