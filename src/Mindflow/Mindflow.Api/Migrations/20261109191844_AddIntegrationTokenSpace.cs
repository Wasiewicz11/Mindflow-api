using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mindflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationTokenSpace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "space_id",
                table: "integration_tokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_integration_tokens_space_id",
                table: "integration_tokens",
                column: "space_id");

            migrationBuilder.AddForeignKey(
                name: "fk_integration_tokens_spaces_space_id",
                table: "integration_tokens",
                column: "space_id",
                principalTable: "spaces",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_integration_tokens_spaces_space_id",
                table: "integration_tokens");

            migrationBuilder.DropIndex(
                name: "ix_integration_tokens_space_id",
                table: "integration_tokens");

            migrationBuilder.DropColumn(
                name: "space_id",
                table: "integration_tokens");
        }
    }
}
