-- Run once after 003, using a DDL-capable account. MySQL 5.5 DDL is not transactional.
USE `account_management`;

CREATE TABLE am_tags (
    id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    owner_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    name VARCHAR(40) COLLATE utf8mb4_bin NOT NULL,
    color VARCHAR(10) CHARACTER SET ascii NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_tag_name (owner_id, name),
    CONSTRAINT fk_tag_user FOREIGN KEY (owner_id) REFERENCES am_users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE am_account_tags (
    account_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    tag_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    PRIMARY KEY (account_id, tag_id),
    CONSTRAINT fk_tag_account FOREIGN KEY (account_id) REFERENCES am_accounts(id) ON DELETE CASCADE,
    CONSTRAINT fk_account_tag FOREIGN KEY (tag_id) REFERENCES am_tags(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE am_account_shares (
    account_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    recipient_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    created_at BIGINT NOT NULL,
    PRIMARY KEY (account_id, recipient_id),
    KEY ix_share_recipient (recipient_id),
    CONSTRAINT fk_share_account FOREIGN KEY (account_id) REFERENCES am_accounts(id) ON DELETE CASCADE,
    CONSTRAINT fk_share_recipient FOREIGN KEY (recipient_id) REFERENCES am_users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
