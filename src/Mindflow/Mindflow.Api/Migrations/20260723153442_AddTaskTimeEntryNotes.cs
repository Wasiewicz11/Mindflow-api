using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mindflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskTimeEntryNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "task_time_entries",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "notes",
                table: "task_time_entries");
        }
    }
}
