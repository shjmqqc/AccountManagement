# 已部署核心后端：邀请和管理接口升级

适用于已经有 002 四张表、管理员和 vault.key 的 `/opt/acc_mgr/backend`。本页说明 003 管理功能迁移：新增 `am_invitations`、`am_audit_logs` 两张表，不修改旧 Spring Boot 服务。Windows 管理界面已接入。

**当前版本还需要 004 分享/标签表。** 已执行过 003 的服务器直接按 [SHARING-UPGRADE.md](SHARING-UPGRADE.md) 升级；从 002 升级时，在完成本页 003 后、启动当前版本 JAR 前，继续执行 004。不要重跑已完成的建表脚本。

## 1. 备份并上传

先备份 account_management 数据库，以及当前 account-management.jar、config.properties 和密钥文件；密钥与数据库备份分开保管。备份文件包含敏感信息，不要放到公开目录或发到聊天中。

```bash
cd /opt/acc_mgr/backend
umask 077
mysqldump -u root -p --single-transaction --default-character-set=utf8mb4 account_management > account_management-before-admin.sql
cp -p account-management.jar account-management.before-admin.jar
```

确认备份命令成功、文件非空后继续。从发布 ZIP 中单独取出 `003-admin-schema.sql` 和新 `account-management.jar`；先把新 JAR 上传为 `account-management.next.jar`。原 lib 依赖版本不变。保留原 config.properties、vault.key，不运行 keygen 或 init-admin，不整目录覆盖。

## 2. 执行一次增量建表

```bash
cd /opt/acc_mgr/backend
mysql -u root -p --default-character-set=utf8mb4 < 003-admin-schema.sql
```

使用有建表权限的数据库用户。不要重跑 001/002，不加 `--force`。MySQL 5.5 DDL 不能整体回滚；如果中途报错，保留错误并先检查两张表的状态，不要直接重复运行或删表。

原先 am_app 在整个 account_management 库上的 SELECT/INSERT/UPDATE/DELETE 授权会覆盖新增表，无需给应用用户建表权限。

## 3. 仅重启帐号管理进程

若当前新工具在前台运行，在它自己的终端按 Ctrl+C 停止；若已有进程管理方式，按原方式仅停止这个帐号管理进程。不要执行 killall java / pkill java，也不要停止旧 Spring Boot 服务。

```bash
cd /opt/acc_mgr/backend
mv account-management.next.jar account-management.jar
java -Xms16m -Xmx128m -cp 'account-management.jar:lib/*' accmgr.Main serve config.properties
```

启动会检查新表是否存在，监听地址按 config.properties 的 http.host/http.port 设置；未设置 http.host 时仍监听 127.0.0.1。需要直接 HTTP 访问时设置 http.host=0.0.0.0，后续 HTTPS 由 Nginx 提供。需要回退时，只停止这个新工具进程并换回备份 JAR；保留新表，旧版本不会使用它们。重置、停用等已提交的数据操作不会因换回 JAR 而撤销。

## 4. 验收

先用已有 Windows 客户端确认管理员登录和原记录读取正常。管理接口请求格式见 API.md；本次只完成接口，尚无对应桌面按钮。

以测试帐号完成：创建邀请 → 注册 → 重复使用邀请失败；新增虚构平台密码 → 管理员只读查看 → 停用后旧会话失效 → 恢复后重新登录 → 重置后使用临时密码登录 → 强制改密 → 原平台密码仍可解密。再核对审计记录，确认没有密码内容。

本机构建已覆盖 Java 8、真实 HTTP 和 H2 事务测试，但这次新增 DDL 及管理流程仍需在你的 MySQL 5.5.34 上验收。上传后先反馈建表/启动错误类别即可，不要发送密码、令牌、邀请码或密钥。
