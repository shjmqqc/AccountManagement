# 分享与标签增量升级

适用：已经部署 002 核心表和 003 管理表的服务器。本次新增三张表，随后更新独立帐号管理服务和 Windows 客户端。不会修改原 Spring Boot 服务。

## 1. 备份并停止帐号管理服务

先退出客户端。使用原来的运行方式，仅停止帐号管理进程；不要执行 `killall java`。在 `/opt/acc_mgr/backend` 备份当前 JAR，并保留原 `config.properties` 和原加密密钥。不要重新生成密钥或初始化管理员。

下面使用应用数据库用户备份；若原来已验证成功的备份方式不同，继续使用原方式。

```bash
cd /opt/acc_mgr/backend
umask 077
mysqldump -h 127.0.0.1 -u am_app -p --single-transaction --skip-lock-tables --default-character-set=utf8mb4 account_management > account_management-before-sharing.sql
```

确认命令成功、备份非空，再继续；备份不要上传到 GitHub。另行安全保存原加密密钥，数据库备份不包含密钥。

## 2. 执行一次 004

从新后端 ZIP 取出 `004-sharing-tags.sql`，使用具备建表权限的数据库管理帐号运行。以下 root 如不可用，应替换为之前成功使用的管理帐号；`am_app` 默认没有建表权限。

```bash
mysql -u root -p < 004-sharing-tags.sql
```

只新增 `am_tags`、`am_account_tags`、`am_account_shares`，不修改既有业务表，不重跑 001/002/003。MySQL 5.5 建表非事务性；任何报错都应停下检查，不能加 `--force` 或直接反复执行。确认三张表全部创建成功后才更新服务。

## 3. 更新并启动

将新的 `account-management.jar` 替换到原部署目录，依赖版本未变。保留原配置、密钥和 lib 目录；不要用示例配置覆盖真实配置。

```bash
java -Xms16m -Xmx128m -cp 'account-management.jar:lib/*' accmgr.Main serve config.properties
```

启动时会检查新增表。随后按原来的进程管理方式运行，仅涉及帐号管理服务。监听地址及 HTTP/Nginx 配置不变。

退出 Windows 旧客户端，将 `AccountManagement-win-x64.zip` 完整解压到新目录，运行其中 EXE。

## 4. 使用虚构数据验收

1. 编辑本人帐号，创建“测试帐号”等不同颜色标签，多选后保存；检查列表、详情及标签筛选。
2. 点击帐号详情的“分享”，输入完整用户名查找，逐个添加两个接收人。
3. 接收人从“分享给我”查看来源、标签和密码，确认不能编辑、删除或再次分享。
4. 所有者更新密码或标签，接收人刷新后检查同步。
5. 撤销其中一个人的授权，确认其刷新后记录消失，后续密码读取被拒绝，另一人仍可访问。

本地 Java/H2 与 Windows 联调已覆盖这些权限流程，但新增 DDL 及持久化仍需在目标 MySQL 5.5 验收。若需回退，停止帐号管理服务，恢复原 JAR 和旧客户端，保留新增表及原密钥；旧版没有分享入口，不会使用新增表。无需删除表或恢复整个数据库。
