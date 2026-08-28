using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mindflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationTokenExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                table: "integration_tokens",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP + INTERVAL '30 days'");

            // The default only backfills existing rows; the model always supplies a value, so it must not linger in the schema.
            migrationBuilder.Sql("ALTER TABLE integration_tokens ALTER COLUMN expires_at DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "integration_tokens");
        }
    }
}
