using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mindflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class BackfillProjectTagsFromTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                WITH extracted AS (
                    SELECT
                        project_id,
                        trim(tag) AS name
                    FROM tasks
                    CROSS JOIN LATERAL unnest(tags) AS tag
                    WHERE project_id IS NOT NULL
                        AND trim(tag) <> ''
                ),
                deduped AS (
                    SELECT DISTINCT ON (project_id, lower(name))
                        project_id,
                        name
                    FROM extracted
                    ORDER BY project_id, lower(name), name
                ),
                missing AS (
                    SELECT
                        project_id,
                        name,
                        md5(project_id::text || ':' || lower(name)) AS hash
                    FROM deduped d
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM project_tags pt
                        WHERE pt.project_id = d.project_id
                            AND lower(pt.name) = lower(d.name)
                    )
                )
                INSERT INTO project_tags (id, project_id, name, created_at)
                SELECT
                    (
                        substr(hash, 1, 8) || '-' ||
                        substr(hash, 9, 4) || '-' ||
                        substr(hash, 13, 4) || '-' ||
                        substr(hash, 17, 4) || '-' ||
                        substr(hash, 21, 12)
                    )::uuid,
                    project_id,
                    name,
                    now()
                FROM missing
                ON CONFLICT (project_id, name) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
