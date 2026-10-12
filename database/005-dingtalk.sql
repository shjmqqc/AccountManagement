-- Run once after 004, with DDL privileges. MySQL 5.5 DDL is not transactional.
USE `account_management`;

CREATE TABLE am_dingtalk_bindings (
    corp_id VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    ding_user_id VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    created_at BIGINT NOT NULL,
    PRIMARY KEY (corp_id, ding_user_id),
    UNIQUE KEY uq_dingtalk_account (corp_id, user_id),
    CONSTRAINT fk_dingtalk_user FOREIGN KEY (user_id) REFERENCES am_users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
