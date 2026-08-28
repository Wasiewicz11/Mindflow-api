using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mindflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationTokenPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "integration_token_id",
                table: "task_activity_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "integration_token_permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    integration_token_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_token_permissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_integration_token_permissions_integration_tokens_integratio",
                        column: x => x.integration_token_id,
                        principalTable: "integration_tokens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_task_activity_events_integration_token_id",
                table: "task_activity_events",
                column: "integration_token_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_token_permissions_integration_token_id_scope",
                table: "integration_token_permissions",
                columns: new[] { "integration_token_id", "scope" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_task_activity_events_integration_tokens_integration_token_id",
                table: "task_activity_events",
                column: "integration_token_id",
                principalTable: "integration_tokens",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // Expand the old bit-flag column into one row per scope before dropping it.
            migrationBuilder.Sql(@"
                INSERT INTO integration_token_permissions (id, integration_token_id, scope)
                SELECT gen_random_uuid(), token.id, flag.scope
                FROM integration_tokens AS token
                JOIN (VALUES
                    (1, 'ProjectsRead'),
                    (2, 'TasksRead'),
                    (4, 'TasksCreate'),
                    (8, 'TasksUpdate'),
                    (16, 'TasksDelete'),
                    (32, 'SubtasksRead'),
                    (64, 'SubtasksCreate'),
                    (128, 'SubtasksUpdate'),
                    (256, 'SubtasksDelete'),
                    (512, 'TimeEntriesRead'),
                    (1024, 'TimeEntriesCreate'),
                    (2048, 'TimeEntriesUpdate'),
                    (4096, 'TimeEntriesDelete')
                ) AS flag(bit, scope) ON (token.access & flag.bit) <> 0;");

            // Older integration events stored the token id in actor_id; move it to its own column.
            migrationBuilder.Sql(@"
                UPDATE task_activity_events
                SET integration_token_id = actor_id,
                    actor_id = user_id
                WHERE actor_type = 'Integration'
                  AND actor_id IN (SELECT id FROM integration_tokens);");

            migrationBuilder.DropColumn(
                name: "access",
                table: "integration_tokens");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_task_activity_events_integration_tokens_integration_token_id",
                table: "task_activity_events");

            migrationBuilder.DropTable(
                name: "integration_token_permissions");

            migrationBuilder.DropIndex(
                name: "ix_task_activity_events_integration_token_id",
                table: "task_activity_events");

            migrationBuilder.DropColumn(
                name: "integration_token_id",
                table: "task_activity_events");

            migrationBuilder.AddColumn<int>(
                name: "access",
                table: "integration_tokens",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
