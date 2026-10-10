-- MySQL 5.5.34. Run once using an administrative account.
-- Deliberately fail if the name already exists; do not use mysql --force.
-- No existing database, global setting or business table is changed.
CREATE DATABASE `account_management`
  DEFAULT CHARACTER SET utf8mb4
  DEFAULT COLLATE utf8mb4_unicode_ci;
