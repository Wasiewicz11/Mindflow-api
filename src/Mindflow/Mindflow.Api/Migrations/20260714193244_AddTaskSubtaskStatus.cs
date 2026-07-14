using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mindflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskSubtaskStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "task_subtasks",
                type: "text",
                nullable: false,
                defaultValue: "NotStarted");

            migrationBuilder.Sql("""
                UPDATE task_subtasks
                SET status = 'Completed'
                WHERE is_completed = TRUE
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "status",
                table: "task_subtasks");
        }
    }
}
