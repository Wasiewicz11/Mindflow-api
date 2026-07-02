using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mindflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGoalDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "goal_days",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    day_short = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    date_label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    marker_level = table.Column<int>(type: "integer", nullable: false),
                    sections = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    linked_task_ids = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goal_days", x => x.id);
                    table.ForeignKey(
                        name: "fk_goal_days_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_goal_days_user_id_date",
                table: "goal_days",
                columns: new[] { "user_id", "date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "goal_days");
        }
    }
}
