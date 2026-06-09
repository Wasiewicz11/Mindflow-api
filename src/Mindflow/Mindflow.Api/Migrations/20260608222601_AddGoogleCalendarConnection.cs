using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mindflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleCalendarConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "google_calendar_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    google_account_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    refresh_token_encrypted = table.Column<string>(type: "text", nullable: false),
                    access_token_encrypted = table.Column<string>(type: "text", nullable: true),
                    access_token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dedicated_calendar_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    source_calendar_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    sync_token = table.Column<string>(type: "text", nullable: true),
                    watch_channel_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    watch_resource_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    watch_token = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    watch_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_google_calendar_connections", x => x.id);
                    table.ForeignKey(
                        name: "fk_google_calendar_connections_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_blocks_user_id_external_event_id",
                table: "calendar_blocks",
                columns: new[] { "user_id", "external_event_id" });

            migrationBuilder.CreateIndex(
                name: "ix_google_calendar_connections_user_id",
                table: "google_calendar_connections",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_google_calendar_connections_watch_channel_id",
                table: "google_calendar_connections",
                column: "watch_channel_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "google_calendar_connections");

            migrationBuilder.DropIndex(
                name: "ix_calendar_blocks_user_id_external_event_id",
                table: "calendar_blocks");
        }
    }
}
