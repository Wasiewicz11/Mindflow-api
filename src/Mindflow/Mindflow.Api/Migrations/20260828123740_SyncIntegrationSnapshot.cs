using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mindflow.Api.Migrations
{
    /// <summary>
    /// No-op. The integration tables were created by AddIntegrations, AddIntegrationTokenExpiry and
    /// AddIntegrationTokenPermissions, which were authored on a branch that forked before the later
    /// develop migrations. This migration only carries the merged model snapshot so the next scaffold
    /// diffs against the real schema.
    /// </summary>
    public partial class SyncIntegrationSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
