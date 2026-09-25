using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Integrations.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntegrationHealthHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "health_checks",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Environment = table.Column<int>(type: "integer", nullable: false),
                    Automatic = table.Column<bool>(type: "boolean", nullable: false),
                    CheckedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TokenExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_health_checks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_health_checks_connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "integrations",
                        principalTable: "connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_health_checks_ConnectionId_CheckedAtUtc",
                schema: "integrations",
                table: "health_checks",
                columns: new[] { "ConnectionId", "CheckedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "health_checks",
                schema: "integrations");
        }
    }
}
