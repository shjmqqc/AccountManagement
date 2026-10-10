-- Run once in the already-created account_management database.
-- Do not use --force or IF NOT EXISTS: stop on unexpected existing tables.
-- MySQL 5.5 DDL is not transactional. Inspect any partial failure before retrying.
USE `account_management`;

CREATE TABLE am_users (
    id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    username VARCHAR(100) COLLATE utf8mb4_bin NOT NULL,
    password_hash VARCHAR(255) CHARACTER SET ascii NOT NULL,
    role VARCHAR(10) CHARACTER SET ascii NOT NULL,
    active INT NOT NULL DEFAULT 1,
    must_change_password INT NOT NULL DEFAULT 0,
    failed_logins INT NOT NULL DEFAULT 0,
    locked_until BIGINT NOT NULL DEFAULT 0,
    created_at BIGINT NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_user_name (username)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE am_platforms (
    id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    owner_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    name VARCHAR(120) NOT NULL,
    url VARCHAR(1000) NOT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_platform_name (owner_id, name),
    UNIQUE KEY uq_platform_owner (id, owner_id),
    CONSTRAINT fk_platform_user FOREIGN KEY (owner_id) REFERENCES am_users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE am_accounts (
    id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    owner_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    platform_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    name VARCHAR(120) NOT NULL,
    login VARCHAR(160) COLLATE utf8mb4_bin NOT NULL,
    password_cipher TEXT CHARACTER SET ascii NOT NULL,
    notes TEXT NOT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_account_login (platform_id, login),
    KEY ix_account_owner (owner_id),
    KEY ix_account_platform_owner (platform_id, owner_id),
    CONSTRAINT fk_account_platform FOREIGN KEY (platform_id, owner_id)
        REFERENCES am_platforms(id, owner_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE am_sessions (
    token_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    expires_at BIGINT NOT NULL,
    PRIMARY KEY (token_hash),
    KEY ix_session_user (user_id),
    KEY ix_session_expiry (expires_at),
    CONSTRAINT fk_session_user FOREIGN KEY (user_id) REFERENCES am_users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
