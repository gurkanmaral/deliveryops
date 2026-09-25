using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Auth.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCourierIdentityLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CourierId",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_CourierId",
                table: "users",
                column: "CourierId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_CourierId",
                table: "users");

            migrationBuilder.DropColumn(
                name: "CourierId",
                table: "users");
        }
    }
}
