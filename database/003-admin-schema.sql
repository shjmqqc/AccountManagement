-- Existing installations: run ONLY this migration once, with a DDL-capable user.
-- MySQL 5.5 DDL is not transactional. Stop and inspect any partial failure.
USE `account_management`;

CREATE TABLE am_invitations (
    id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    code_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    created_by CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    created_at BIGINT NOT NULL,
    expires_at BIGINT NOT NULL,
    revoked_at BIGINT NOT NULL DEFAULT 0,
    used_at BIGINT NOT NULL DEFAULT 0,
    used_by CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_invitation_code (code_hash),
    KEY ix_invitation_created (created_at, id),
    CONSTRAINT fk_invitation_creator FOREIGN KEY (created_by) REFERENCES am_users(id),
    CONSTRAINT fk_invitation_user FOREIGN KEY (used_by) REFERENCES am_users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE am_audit_logs (
    id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    actor_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    target_user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
    target_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
    action VARCHAR(40) CHARACTER SET ascii NOT NULL,
    outcome VARCHAR(10) CHARACTER SET ascii NOT NULL,
    created_at BIGINT NOT NULL,
    PRIMARY KEY (id),
    KEY ix_audit_created (created_at, id),
    CONSTRAINT fk_audit_actor FOREIGN KEY (actor_id) REFERENCES am_users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
