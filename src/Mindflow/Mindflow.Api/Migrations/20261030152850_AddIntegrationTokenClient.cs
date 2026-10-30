using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mindflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationTokenClient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "client_id",
                table: "integration_tokens",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "default_project_id",
                table: "integration_tokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_integration_tokens_user_id_client_id",
                table: "integration_tokens",
                columns: new[] { "user_id", "client_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_integration_tokens_user_id_client_id",
                table: "integration_tokens");

            migrationBuilder.DropColumn(
                name: "client_id",
                table: "integration_tokens");

            migrationBuilder.DropColumn(
                name: "default_project_id",
                table: "integration_tokens");
        }
    }
}
