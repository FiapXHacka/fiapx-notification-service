-- notification_db — owner: notification-service (ADR-0009)
-- Same schema as the EF Core migration InitialCreate (src/FiapX.Notification.Infrastructure/Persistence/Migrations).
-- Databases and roles are created by fiapx-infra-db (fiapx/docs/database/00-create-databases.sql).

-- Local projection of user.registered (ADR-0012)
CREATE TABLE user_contacts (
    user_id     uuid        PRIMARY KEY,
    email       text        NOT NULL,
    name        text        NOT NULL,
    updated_at  timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE notifications (
    id          uuid        PRIMARY KEY,
    user_id     uuid        NOT NULL,
    video_id    uuid        NOT NULL,
    type        text        NOT NULL,
    channel     text        NOT NULL DEFAULT 'EMAIL',
    status      text        NOT NULL DEFAULT 'PENDING',
    attempts    int         NOT NULL DEFAULT 0,
    last_error  text,
    created_at  timestamptz NOT NULL DEFAULT now(),
    sent_at     timestamptz,
    CONSTRAINT ck_notifications_type   CHECK (type IN ('VIDEO_FAILED','VIDEO_COMPLETED')),
    CONSTRAINT ck_notifications_status CHECK (status IN ('PENDING','SENT','FAILED'))
);
-- never two e-mails for the same event
CREATE UNIQUE INDEX ux_notifications_video_type ON notifications (video_id, type);
CREATE INDEX ix_notifications_user ON notifications (user_id, created_at DESC);

CREATE TABLE inbox_messages (
    message_id    uuid        NOT NULL,
    consumer      text        NOT NULL,
    processed_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (message_id, consumer)
);
