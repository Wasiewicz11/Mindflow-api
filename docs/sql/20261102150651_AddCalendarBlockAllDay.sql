START TRANSACTION;
ALTER TABLE calendar_blocks ADD is_all_day boolean NOT NULL DEFAULT FALSE;

UPDATE google_calendar_connections SET sync_token = NULL;

INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20261102150651_AddCalendarBlockAllDay', '10.0.6');

COMMIT;


