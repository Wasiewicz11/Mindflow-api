using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mindflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSubtaskEstimatesAndTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "subtask_id",
                table: "task_time_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "estimated_hours",
                table: "task_subtasks",
                type: "numeric(6,2)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_time_entries_subtask_id",
                table: "task_time_entries",
                column: "subtask_id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_time_entries_task_subtasks_subtask_id",
                table: "task_time_entries",
                column: "subtask_id",
                principalTable: "task_subtasks",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_task_time_entries_task_subtasks_subtask_id",
                table: "task_time_entries");

            migrationBuilder.DropIndex(
                name: "ix_task_time_entries_subtask_id",
                table: "task_time_entries");

            migrationBuilder.DropColumn(
                name: "subtask_id",
                table: "task_time_entries");

            migrationBuilder.DropColumn(
                name: "estimated_hours",
                table: "task_subtasks");
        }
    }
}
