using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mindflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "calendar_blocks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    external_event_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    google_calendar_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    sync_status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_blocks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_blocks_task_id",
                table: "calendar_blocks",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_blocks_user_id",
                table: "calendar_blocks",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_blocks_user_id_start_at",
                table: "calendar_blocks",
                columns: new[] { "user_id", "start_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calendar_blocks");
        }
    }
}
