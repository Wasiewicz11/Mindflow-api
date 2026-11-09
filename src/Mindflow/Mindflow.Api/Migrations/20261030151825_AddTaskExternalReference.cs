using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mindflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskExternalReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_id",
                table: "tasks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_source",
                table: "tasks",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_url",
                table: "tasks",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tasks_user_id_external_source_external_id",
                table: "tasks",
                columns: new[] { "user_id", "external_source", "external_id" },
                unique: true,
                filter: "external_source IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tasks_user_id_external_source_external_id",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "external_id",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "external_source",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "external_url",
                table: "tasks");
        }
    }
}
