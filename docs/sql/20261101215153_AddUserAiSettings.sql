START TRANSACTION;
ALTER TABLE ai_usage_daily ADD dictation_calls integer NOT NULL DEFAULT 0;

CREATE TABLE user_ai_settings (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    provider character varying(20) NOT NULL,
    api_key_encrypted text,
    api_key_last4 character varying(4),
    model character varying(100),
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_user_ai_settings PRIMARY KEY (id),
    CONSTRAINT fk_user_ai_settings_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX ix_user_ai_settings_user_id ON user_ai_settings (user_id);

INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20261101215153_AddUserAiSettings', '10.0.6');

COMMIT;

