using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mindflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBrainGraph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "brain_maps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brain_maps", x => x.id);
                    table.ForeignKey(
                        name: "fk_brain_maps_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "brain_edges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    brain_map_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    from_node_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    to_node_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    label = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brain_edges", x => x.id);
                    table.ForeignKey(
                        name: "fk_brain_edges_brain_maps_brain_map_id",
                        column: x => x.brain_map_id,
                        principalTable: "brain_maps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "brain_nodes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    brain_map_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    label = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    x = table.Column<double>(type: "double precision", nullable: false),
                    y = table.Column<double>(type: "double precision", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    accent = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    source_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brain_nodes", x => x.id);
                    table.ForeignKey(
                        name: "fk_brain_nodes_brain_maps_brain_map_id",
                        column: x => x.brain_map_id,
                        principalTable: "brain_maps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_brain_edges_brain_map_id_from_node_key",
                table: "brain_edges",
                columns: new[] { "brain_map_id", "from_node_key" });

            migrationBuilder.CreateIndex(
                name: "ix_brain_edges_brain_map_id_key",
                table: "brain_edges",
                columns: new[] { "brain_map_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_brain_edges_brain_map_id_to_node_key",
                table: "brain_edges",
                columns: new[] { "brain_map_id", "to_node_key" });

            migrationBuilder.CreateIndex(
                name: "ix_brain_maps_user_id_key",
                table: "brain_maps",
                columns: new[] { "user_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_brain_nodes_brain_map_id_key",
                table: "brain_nodes",
                columns: new[] { "brain_map_id", "key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "brain_edges");

            migrationBuilder.DropTable(
                name: "brain_nodes");

            migrationBuilder.DropTable(
                name: "brain_maps");
        }
    }
}
