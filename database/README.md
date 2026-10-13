# 数据库最小验证

目标环境：MySQL 5.5.34、Java 8。此阶段仅创建独立空库和只读探针用户，不创建正式业务表，不接触旧业务库，也不调整全局字符集或重启服务。

## 1. 一次性建库

将 `001-create-database.sql` 上传至服务器新工具的独立目录。在该目录运行：

```bash
mysql -u root -p < 001-create-database.sql
```

也可以使用具备建库权限的管理帐号。如果提示数据库已存在，停止并核查归属，不删除、不覆盖，不加 `--force`。只有建库成功后才继续下一步。

## 2. 创建只读验证用户

打开不保存客户端 SQL 历史的交互会话：

```bash
MYSQL_HISTFILE=/dev/null mysql -u root -p
```

下面是模板，不要直接原样执行。先将 `REPLACE_WITH_RANDOM_PASSWORD` 替换为自行保存的独立随机密码（建议 24 位以上字母数字），不使用旧业务或 root 密码，不把密码发到聊天中。服务器端已有审计日志仍可能记录管理操作。

```sql
CREATE USER 'am_probe'@'127.0.0.1' IDENTIFIED BY 'REPLACE_WITH_RANDOM_PASSWORD';
GRANT SELECT ON `account\_management`.* TO 'am_probe'@'127.0.0.1';
SHOW GRANTS FOR 'am_probe'@'127.0.0.1';
exit;
```

GRANT 中下划线被转义，避免 MySQL 数据库权限通配符扩大授权范围。不创建 `%` 主机用户，不开放 3306 公网端口。该帐号仅供本机连接验证，正式应用用户及权限后续单独创建。

## 3. 运行探针

上传 `database-probe.jar` 与 `mysql-connector-java-5.1.49.jar` 到服务器同一独立目录，在交互式 SSH 终端执行：

```bash
java -Xms16m -Xmx64m -cp 'database-probe.jar:mysql-connector-java-5.1.49.jar' DatabaseProbe
```

程序提示后输入上一步的探针用户密码，输入不回显。默认连接 `127.0.0.1:3306`；非默认端口可追加端口数字。程序只执行 SELECT，核对数据库、连接字符集，以及中文和四字节字符的查询往返，完成后退出。

成功时输出服务端版本和 PASS。将结果发回即可，无须发送密码。错误只显示 SQLState 和错误码。

`character_set_results=null` 是允许的：Connector/J 可关闭服务器端结果转换，按各列元数据解码。探针仍严格检查数据库、客户端及连接字符集为 utf8mb4，并通过中文及四字节字符的实际查询往返验证解码。旧版探针对第 6 列一律要求 utf8mb4，会误拒绝这一正常设置；需替换为修正后的 JAR。

此验证不证明表存储、业务权限、密码加密或正式后端已经通过验证。本机编译也不等于已在服务器验证成功。

## 管理接口增量表

邀请和审计使用 `003-admin-schema.sql` 新增两张表。已部署核心后端时只执行 003，不重跑 001/002，不改旧业务数据库或全局字符集。具体备份、升级和回退步骤见 [后端升级说明](../backend/UPGRADE.md)。新安装需要先执行 002，再执行 003。

## 分享与标签增量表

分享与标签使用 `004-sharing-tags.sql` 新增标签目录、帐号标签关联及分享授权三张表。新安装依次执行 002、003、004；已部署管理功能只需执行 004。详见 [分享与标签升级说明](../backend/SHARING-UPGRADE.md)。不修改全局字符集或旧业务库，不重新生成密钥。

## 驱动与构建

当前版本安装须在 004 后继续执行 `005-dingtalk.sql` 和 `006-mobile-search.sql`，包括未启用钉钉免登的部署。006 添加绑定姓名，并将帐号判重改为同平台、同登录帐号、同名称；兼容 MySQL 5.5 的判重事务与升级步骤见 [H5 查找与分享升级](../backend/MOBILE-SEARCH-UPGRADE.md)。已有部署只执行缺少的迁移，不重复建表。

探针暂使用 Connector/J 5.1.49 适配旧 MySQL；该系列已结束常规维护，仅作为现有旧环境的兼容性选择，不是推荐给新环境的默认版本。

- 官方兼容性文档：https://downloads.mysql.com/docs/connector-j-5.1-en.pdf
- 驱动来源：https://repo.maven.apache.org/maven2/mysql/mysql-connector-java/5.1.49/mysql-connector-java-5.1.49.jar

在 `probes/java8` 目录用 JDK 8 编译，无需 Maven：

```powershell
New-Item -ItemType Directory -Force build | Out-Null
& 'C:\Program Files\Java\jdk1.8.0_192\bin\javac.exe' -encoding UTF-8 -d build DatabaseProbe.java
& 'C:\Program Files\Java\jdk1.8.0_192\bin\jar.exe' cfe build/database-probe.jar DatabaseProbe -C build DatabaseProbe.class
```
