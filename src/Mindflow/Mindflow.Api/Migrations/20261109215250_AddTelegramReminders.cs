using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mindflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "telegram_reminders_enabled",
                table: "notification_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "telegram_connections",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: true),
                    username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    link_code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    link_code_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_telegram_connections", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_telegram_connections_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_telegram_connections_chat_id",
                table: "telegram_connections",
                column: "chat_id",
                unique: true,
                filter: "chat_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_telegram_connections_link_code_hash",
                table: "telegram_connections",
                column: "link_code_hash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "telegram_connections");

            migrationBuilder.DropColumn(
                name: "telegram_reminders_enabled",
                table: "notification_settings");
        }
    }
}
