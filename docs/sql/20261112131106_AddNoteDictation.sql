START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261112131106_AddNoteDictation') THEN
    CREATE TABLE note_dictation_jobs (
        note_id uuid NOT NULL,
        user_id uuid NOT NULL,
        audio bytea,
        audio_mime_type character varying(64),
        text text,
        lite boolean NOT NULL,
        attempts integer NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_note_dictation_jobs PRIMARY KEY (note_id),
        CONSTRAINT fk_note_dictation_jobs_notes_note_id FOREIGN KEY (note_id) REFERENCES notes (id) ON DELETE CASCADE,
        CONSTRAINT fk_note_dictation_jobs_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261112131106_AddNoteDictation') THEN
    CREATE TABLE note_task_suggestions (
        id uuid NOT NULL,
        note_id uuid NOT NULL,
        "order" integer NOT NULL,
        title character varying(200) NOT NULL,
        description text,
        due_date date,
        due_time time without time zone,
        priority text NOT NULL,
        project_id uuid,
        subtasks jsonb NOT NULL,
        status character varying(16) NOT NULL,
        task_id uuid,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_note_task_suggestions PRIMARY KEY (id),
        CONSTRAINT fk_note_task_suggestions_notes_note_id FOREIGN KEY (note_id) REFERENCES notes (id) ON DELETE CASCADE,
        CONSTRAINT fk_note_task_suggestions_projects_project_id FOREIGN KEY (project_id) REFERENCES projects (id) ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261112131106_AddNoteDictation') THEN
    CREATE INDEX ix_note_dictation_jobs_user_id ON note_dictation_jobs (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261112131106_AddNoteDictation') THEN
    CREATE INDEX ix_note_task_suggestions_note_id_order ON note_task_suggestions (note_id, "order");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261112131106_AddNoteDictation') THEN
    CREATE INDEX ix_note_task_suggestions_project_id ON note_task_suggestions (project_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261112131106_AddNoteDictation') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261112131106_AddNoteDictation', '10.0.6');
    END IF;
END $EF$;
COMMIT;

