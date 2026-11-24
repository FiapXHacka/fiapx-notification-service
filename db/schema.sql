CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261123195551_InitialCreate') THEN
    CREATE TABLE inbox_messages (
        message_id uuid NOT NULL,
        consumer text NOT NULL,
        processed_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_inbox_messages PRIMARY KEY (message_id, consumer)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261123195551_InitialCreate') THEN
    CREATE TABLE notifications (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        video_id uuid NOT NULL,
        type text NOT NULL,
        channel text NOT NULL DEFAULT ('EMAIL'),
        status text NOT NULL DEFAULT ('PENDING'),
        attempts integer NOT NULL DEFAULT 0,
        last_error text,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        sent_at timestamp with time zone,
        CONSTRAINT pk_notifications PRIMARY KEY (id),
        CONSTRAINT ck_notifications_status CHECK (status IN ('PENDING','SENT','FAILED')),
        CONSTRAINT ck_notifications_type CHECK (type IN ('VIDEO_FAILED','VIDEO_COMPLETED'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261123195551_InitialCreate') THEN
    CREATE TABLE user_contacts (
        user_id uuid NOT NULL,
        email text NOT NULL,
        name text NOT NULL,
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT pk_user_contacts PRIMARY KEY (user_id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261123195551_InitialCreate') THEN
    CREATE INDEX ix_notifications_user ON notifications (user_id, created_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261123195551_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_notifications_video_type ON notifications (video_id, type);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261123195551_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20261123195551_InitialCreate', '10.0.15');
    END IF;
END $EF$;
COMMIT;

