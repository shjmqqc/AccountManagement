-- Apply once after 005-dingtalk.sql, with the account-management service stopped.
USE `account_management`;
ALTER TABLE am_dingtalk_bindings ADD COLUMN ding_name VARCHAR(100) NOT NULL DEFAULT '';
-- The owner row lock in Service.authenticate serializes account writes.
-- Check (platform_id, login, name) in the transaction to avoid MySQL 5.5's 767-byte index limit.
ALTER TABLE am_accounts DROP INDEX uq_account_login;
CREATE INDEX ix_account_login ON am_accounts(platform_id, login);
