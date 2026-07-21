using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mindflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPushNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_settings",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    morning_brief_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    morning_brief_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    midday_brief_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    midday_brief_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    evening_summary_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    evening_summary_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    block_reminders_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    block_reminder_minutes = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_settings", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_notification_settings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "push_notification_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_notification_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_push_notification_deliveries_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "push_notification_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    p256dh = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    auth = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_notification_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_push_notification_subscriptions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_push_notification_deliveries_sent_at",
                table: "push_notification_deliveries",
                column: "sent_at");

            migrationBuilder.CreateIndex(
                name: "ix_push_notification_deliveries_user_id_delivery_key",
                table: "push_notification_deliveries",
                columns: new[] { "user_id", "delivery_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_push_notification_subscriptions_endpoint",
                table: "push_notification_subscriptions",
                column: "endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_push_notification_subscriptions_user_id",
                table: "push_notification_subscriptions",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_settings");

            migrationBuilder.DropTable(
                name: "push_notification_deliveries");

            migrationBuilder.DropTable(
                name: "push_notification_subscriptions");
        }
    }
}
