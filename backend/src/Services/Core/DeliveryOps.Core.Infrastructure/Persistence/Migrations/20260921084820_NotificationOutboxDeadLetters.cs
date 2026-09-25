using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryOps.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationOutboxDeadLetters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notification_outbox_ProcessedAtUtc_NextAttemptAtUtc",
                table: "notification_outbox");

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessId",
                table: "notification_outbox",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeadLetteredAtUtc",
                table: "notification_outbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastHttpStatusCode",
                table: "notification_outbox",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrderId",
                table: "notification_outbox",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql("""
                UPDATE notification_outbox
                SET "BusinessId" = ("PayloadJson" ->> 'BusinessId')::uuid,
                    "OrderId" = ("PayloadJson" ->> 'OrderId')::uuid
                WHERE "PayloadJson" ? 'BusinessId' AND "PayloadJson" ? 'OrderId';

                UPDATE notification_outbox
                SET "DeadLetteredAtUtc" = NOW(),
                    "ProcessingAtUtc" = NULL
                WHERE "ProcessedAtUtc" IS NULL AND "Attempts" >= 8;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_notification_outbox_ProcessedAtUtc_DeadLetteredAtUtc_NextAt~",
                table: "notification_outbox",
                columns: new[] { "ProcessedAtUtc", "DeadLetteredAtUtc", "NextAttemptAtUtc" },
                filter: "\"ProcessedAtUtc\" IS NULL AND \"DeadLetteredAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notification_outbox_ProcessedAtUtc_DeadLetteredAtUtc_NextAt~",
                table: "notification_outbox");

            migrationBuilder.DropColumn(
                name: "BusinessId",
                table: "notification_outbox");

            migrationBuilder.DropColumn(
                name: "DeadLetteredAtUtc",
                table: "notification_outbox");

            migrationBuilder.DropColumn(
                name: "LastHttpStatusCode",
                table: "notification_outbox");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "notification_outbox");

            migrationBuilder.CreateIndex(
                name: "IX_notification_outbox_ProcessedAtUtc_NextAttemptAtUtc",
                table: "notification_outbox",
                columns: new[] { "ProcessedAtUtc", "NextAttemptAtUtc" });
        }
    }
}
