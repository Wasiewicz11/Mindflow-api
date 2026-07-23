using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Mindflow.Api.Data;

#nullable disable

namespace Mindflow.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MindflowDbContext))]
    [Migration("20260723080649_AddTaskTimeEntries")]
    public partial class AddTaskTimeEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "task_time_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    task_content = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    task_priority = table.Column<string>(type: "text", nullable: false),
                    task_status = table.Column<string>(type: "text", nullable: false),
                    tags = table.Column<List<string>>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    estimated_hours = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_time_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_time_entries_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_task_time_entries_project_id",
                table: "task_time_entries",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_time_entries_task_id",
                table: "task_time_entries",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_time_entries_user_id",
                table: "task_time_entries",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_time_entries_user_id_project_id",
                table: "task_time_entries",
                columns: new[] { "user_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_time_entries_user_id_task_id",
                table: "task_time_entries",
                columns: new[] { "user_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_time_entries_user_id_work_date",
                table: "task_time_entries",
                columns: new[] { "user_id", "work_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_time_entries");
        }
    }
}
