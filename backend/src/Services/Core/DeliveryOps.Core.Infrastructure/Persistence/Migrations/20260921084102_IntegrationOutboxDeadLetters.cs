using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntegrationOutboxDeadLetters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_integration_outbox_ProcessedAtUtc_NextAttemptAtUtc",
                table: "integration_outbox");

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessId",
                table: "integration_outbox",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeadLetteredAtUtc",
                table: "integration_outbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastHttpStatusCode",
                table: "integration_outbox",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrderId",
                table: "integration_outbox",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql("""
                UPDATE integration_outbox
                SET "BusinessId" = ("PayloadJson" ->> 'BusinessId')::uuid,
                    "OrderId" = ("PayloadJson" ->> 'OrderId')::uuid
                WHERE "PayloadJson" ? 'BusinessId' AND "PayloadJson" ? 'OrderId';

                UPDATE integration_outbox
                SET "DeadLetteredAtUtc" = NOW(),
                    "ProcessingAtUtc" = NULL,
                    "LastHttpStatusCode" = CASE
                        WHEN "LastError" LIKE '%409 (Conflict)%' THEN 409
                        ELSE NULL
                    END
                WHERE "ProcessedAtUtc" IS NULL AND "Attempts" >= 8;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_integration_outbox_ProcessedAtUtc_DeadLetteredAtUtc_NextAtt~",
                table: "integration_outbox",
                columns: new[] { "ProcessedAtUtc", "DeadLetteredAtUtc", "NextAttemptAtUtc" },
                filter: "\"ProcessedAtUtc\" IS NULL AND \"DeadLetteredAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_integration_outbox_ProcessedAtUtc_DeadLetteredAtUtc_NextAtt~",
                table: "integration_outbox");

            migrationBuilder.DropColumn(
                name: "BusinessId",
                table: "integration_outbox");

            migrationBuilder.DropColumn(
                name: "DeadLetteredAtUtc",
                table: "integration_outbox");

            migrationBuilder.DropColumn(
                name: "LastHttpStatusCode",
                table: "integration_outbox");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "integration_outbox");

            migrationBuilder.CreateIndex(
                name: "IX_integration_outbox_ProcessedAtUtc_NextAttemptAtUtc",
                table: "integration_outbox",
                columns: new[] { "ProcessedAtUtc", "NextAttemptAtUtc" });
        }
    }
}
