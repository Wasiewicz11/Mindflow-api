START TRANSACTION;
ALTER TABLE integration_tokens ADD space_id uuid;

CREATE INDEX ix_integration_tokens_space_id ON integration_tokens (space_id);

ALTER TABLE integration_tokens ADD CONSTRAINT fk_integration_tokens_spaces_space_id FOREIGN KEY (space_id) REFERENCES spaces (id) ON DELETE CASCADE;

INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20261109191844_AddIntegrationTokenSpace', '10.0.6');

COMMIT;


