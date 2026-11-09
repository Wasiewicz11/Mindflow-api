using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mindflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "reminder_disabled",
                table: "tasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "reminder_minutes_before",
                table: "tasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reminder_snoozed_until",
                table: "tasks",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reminder_disabled",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "reminder_minutes_before",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "reminder_snoozed_until",
                table: "tasks");
        }
    }
}
