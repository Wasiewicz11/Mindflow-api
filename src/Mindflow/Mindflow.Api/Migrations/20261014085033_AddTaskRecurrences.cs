using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mindflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskRecurrences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "recurrence_occurrence_date",
                table: "tasks",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "recurring_task_id",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "task_recurrences",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    frequency = table.Column<string>(type: "text", nullable: false),
                    interval = table.Column<int>(type: "integer", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    days_of_week = table.Column<List<int>>(type: "integer[]", nullable: false, defaultValueSql: "'{}'::integer[]"),
                    day_of_month = table.Column<int>(type: "integer", nullable: true),
                    month_of_year = table.Column<int>(type: "integer", nullable: true),
                    due_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    notifications_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    reminder_minutes_before = table.Column<int>(type: "integer", nullable: false),
                    notification_window_start = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    notification_window_end = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_recurrences", x => x.task_id);
                    table.ForeignKey(
                        name: "fk_task_recurrences_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_recurring_task_id_recurrence_occurrence_date",
                table: "tasks",
                columns: new[] { "recurring_task_id", "recurrence_occurrence_date" },
                unique: true,
                filter: "recurring_task_id IS NOT NULL AND recurrence_occurrence_date IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_task_recurrences_is_enabled_start_date",
                table: "task_recurrences",
                columns: new[] { "is_enabled", "start_date" });

            migrationBuilder.AddForeignKey(
                name: "fk_tasks_tasks_recurring_task_id",
                table: "tasks",
                column: "recurring_task_id",
                principalTable: "tasks",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tasks_tasks_recurring_task_id",
                table: "tasks");

            migrationBuilder.DropTable(
                name: "task_recurrences");

            migrationBuilder.DropIndex(
                name: "ix_tasks_recurring_task_id_recurrence_occurrence_date",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "recurrence_occurrence_date",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "recurring_task_id",
                table: "tasks");
        }
    }
}
