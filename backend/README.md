# 帐号管理最小后端

本阶段实现 Windows 与小程序共同使用的核心 API，尚非完整 V1 产品。服务器既有服务不需要修改或重启。

## 已实现与尚未实现

已实现：一次性管理员初始化、登录/退出/改密、7 天会话、本人平台与帐号增删改查、跨平台搜索、显式密码读取、数据隔离、重复记录约束、禁止删除非空平台、最后保存覆盖。

另已实现：邀请注册、管理员停用/恢复/重置、他人数据只读查看、密码查看/复制审计及审计查询。Windows 核心及管理界面已接入；小程序、Nginx HTTPS 部署和备份自动化尚未完成。本人接口仍不能越权读写他人数据。

另已实现单帐号多接收人只读分享、手动撤销、自定义彩色标签及筛选，Windows 已接入。接收人通过专用接口读取已授权的帐号和密码。

**已安装管理功能的服务器请按 [SHARING-UPGRADE.md](SHARING-UPGRADE.md) 升级，只执行 004 增量建表并更新 JAR，不重跑以下首次部署流程。** 尚未安装 003 时先参考 [UPGRADE.md](UPGRADE.md)。

HTTP 监听地址由 `config.properties` 的 `http.host` 指定，示例配置为 `0.0.0.0`，端口由 `http.port` 指定（默认 18082）。旧配置未填写 http.host 时仍监听 127.0.0.1。后端不强制 HTTPS，后续由 Nginx 终止 TLS 并反向代理到此 HTTP 服务。

## 监听地址配置

只替换 account-management.jar，并在现有 config.properties 中加入或修改以下两项（不要覆盖数据库密码或密钥路径）：

```properties
http.host=0.0.0.0
http.port=18082
```

仅重启帐号管理进程使配置生效，不需要重新建表或生成密钥。客户端使用 `http://服务器IP:18082`；0.0.0.0 是监听地址，不是客户端连接地址。若连接不通，检查云安全组/防火墙是否允许 TCP 18082。HTTP 链路不加密；后续 Nginx 提供 HTTPS 后，客户端改填 `https://你的域名`，同机 Nginx 可代理 `http://127.0.0.1:18082`，并将后端 http.host 收回 127.0.0.1。此次未安装或修改 Nginx。

## 技术与保护措施

- Java 8、JDK HTTP 服务、Gson 2.13.2、Connector/J 5.1.49；不依赖服务器安装 Maven、Docker 或新运行时。
- 4 个工作线程、32 项任务队列；拥塞时由接收任务线程执行以施加背压。每个请求短连接访问本机数据库，不保持大型连接池。
- 登录密码使用随机盐的 PBKDF2-HMAC-SHA256（600,000 次）；平台密码使用 AES-256-GCM，随机 nonce，以用户与记录 ID 作为附加认证数据。
- 密钥在独立文件中，数据库仅保存密文。部署后不能重新生成/覆盖密钥；丢失密钥将无法还原已有平台密码。
- 会话令牌由安全随机数生成，数据库仅存 SHA-256 摘要。改密使所有会话失效；过期会话在成功登录时清理。
- 单帐号连续失败 5 次锁定 15 分钟；另设每进程每分钟 60 次登录和注册尝试共享上限。这些为初始技术默认值，后续可结合实际使用评估。
- 每用户业务请求使用数据库行锁串行化，事务采用 READ COMMITTED；后成功保存覆盖之前内容，不提供版本冲突提示。
- 输入 JSON 最大 16 KiB，不记录请求内容、令牌或密码；响应标记 no-store。拒绝带 Origin 的浏览器请求；原生客户端不发送 Origin。微信小程序真机联网行为留待集成验证。
- 平台名 1–120 字符、登录帐号 1–160 字符、帐号名称 0–120、备注 0–2000、平台密码 0–1024、登录密码 6–256、用户名 1–100；这些上限为实现边界，未改变需求最低长度。
- 平台名称按数据库 unicode_ci 比较（通常不区分大小写）；用户名及登录帐号按 utf8mb4_bin 比较。字符串前后空白在用户名、平台名及登录帐号上会被去除。

## 本机构建和测试

在 backend 目录执行 PowerShell：

```powershell
.\build.ps1
```

可通过 `-Jdk` 指定 JDK 8 路径。首次构建从 Maven Central 下载固定版本依赖并比对仓库提供的校验值。不修改系统 PATH 或 Java 配置。

输出为 `build/account-management.jar` 及 `build/lib` 两个依赖 JAR。H2 2.2.224 仅用于测试，不应上传或用于生产。测试执行实际 HTTP 请求、JDBC 事务和加密操作；H2 的 MySQL 模式不能证明 MySQL 5.5 的实际 DDL、排序规则和持久化已通过。

## 服务器准备（尚未执行）

建议独立目录 `/opt/acc_mgr/backend`，不要覆盖旧 Spring Boot 服务的目录。上传以下文件：

```text
backend/
  account-management.jar
  lib/gson-2.13.2.jar
  lib/mysql-connector-java-5.1.49.jar
  config.example.properties
  002-core-schema.sql
  003-admin-schema.sql
  004-sharing-tags.sql
```

1. 管理员运行建表脚本：

```bash
mysql -u root -p < 002-core-schema.sql
mysql -u root -p < 003-admin-schema.sql
mysql -u root -p < 004-sharing-tags.sql
```

仅在已确认的新库 account_management 执行，不添加 `--force`。002 建四张核心表，003 建两张管理表，004 建三张分享/标签表，不删表、不修改旧业务库。每条命令成功后才执行下一条；MySQL 5.5 DDL 非事务性，任何失败都应先检查报错，不能直接反复执行或删表。

2. 创建独立应用用户；下列密码占位必须自行替换为独立随机字母数字密码。不要使用 root、旧服务或 am_probe 帐号运行后端。

```bash
MYSQL_HISTFILE=/dev/null mysql -u root -p
```

```sql
CREATE USER 'am_app'@'127.0.0.1' IDENTIFIED BY 'REPLACE_WITH_RANDOM_PASSWORD';
GRANT SELECT, INSERT, UPDATE, DELETE ON `account\_management`.* TO 'am_app'@'127.0.0.1';
exit;
```

不授予建库、建表或修改权限，不创建通配主机用户。原 am_probe 保持只读。

3. 创建配置和密钥：

```bash
cd /opt/acc_mgr/backend
umask 077
cp -n config.example.properties config.properties
java -Xms16m -Xmx128m -cp 'account-management.jar:lib/*' accmgr.Main keygen vault.key
chmod 600 config.properties vault.key
```

编辑 config.properties，填写 db.password 和 vault.key 的绝对路径。密码不写入启动命令。建议使用字母数字数据库密码，避免 Java properties 的反斜杠转义。密钥创建命令拒绝覆盖已有文件；不要删除旧密钥来强行重建。将密钥和数据库备份分别安全保存。

4. 首次初始化管理员（在交互式 SSH 终端输入用户名和密码）：

```bash
java -Xms16m -Xmx128m -cp 'account-management.jar:lib/*' accmgr.Main init-admin config.properties
```

重复初始化会被拒绝，无默认管理员密码。

5. 确认 18082 未被占用后，在前台启动测试服务：

```bash
java -Xms16m -Xmx128m -cp 'account-management.jar:lib/*' accmgr.Main serve config.properties
```

另一个 SSH 终端执行 `curl http://127.0.0.1:18082/health`。Ctrl+C 仅停止本次前台进程。不要使用 killall java/pkill java，不配置自启动。128 MB 是堆上限，不是进程总内存上限；需要在真实服务器观察资源占用后再决定长期运行方式。

接口约定见 API.md。登录及密码数据只能在服务器本机测试，尚无 HTTPS 时不开放端口。

## 服务器端业务自检

保持后端前台运行，在第二个 SSH 终端进入同一目录执行：

```bash
java -Xms16m -Xmx64m -cp 'account-management.jar:lib/*' accmgr.SmokeCheck
```

交互输入刚初始化的管理员用户名和密码。程序只连接 127.0.0.1:18082；非默认端口可追加端口数字。
自检会创建一个带随机后缀的测试平台和一条虚构帐号，验证中文/四字节字符实际写入、读取、重新登录后解密，再删除本次创建的帐号和空平台并退出登录。不会删除或修改预先存在的数据。
如果清理失败，会显示本次测试记录 ID，需要随后处理；不要使用批量清库命令。
把 PASS 或错误输出发回即可，不发送密码或配置内容。该检查通过后才能认定 MySQL 5.5 业务持久化已初步验证；重启恢复、HTTPS 和资源占用仍需继续验收。

## 依据

- https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html
- https://cheatsheetseries.owasp.org/cheatsheets/Cryptographic_Storage_Cheat_Sheet.html
- https://google.github.io/gson/
- https://downloads.mysql.com/docs/connector-j-5.1-en.pdf
