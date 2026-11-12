START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261112121730_AddNotes') THEN
    CREATE TABLE notes (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        client_id character varying(64),
        title character varying(200) NOT NULL,
        content text NOT NULL,
        tags text[] NOT NULL DEFAULT ('{}'::text[]),
        project_id uuid,
        source character varying(16) NOT NULL,
        status character varying(16) NOT NULL,
        transcript text,
        event_date date,
        reviewed_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_notes PRIMARY KEY (id),
        CONSTRAINT fk_notes_projects_project_id FOREIGN KEY (project_id) REFERENCES projects (id) ON DELETE SET NULL,
        CONSTRAINT fk_notes_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261112121730_AddNotes') THEN
    CREATE INDEX ix_notes_project_id ON notes (project_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261112121730_AddNotes') THEN
    CREATE UNIQUE INDEX ix_notes_user_id_client_id ON notes (user_id, client_id) WHERE client_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261112121730_AddNotes') THEN
    CREATE INDEX ix_notes_user_id_updated_at ON notes (user_id, updated_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261112121730_AddNotes') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261112121730_AddNotes', '10.0.6');
    END IF;
END $EF$;
COMMIT;

