# 部署地址配置

从本次源码构建开始，部署者无需修改 C# 或 JavaScript 业务代码即可指定自己的服务器。已发布的 v1.1.8 不包含这些新配置入口。

## Windows

修改 `AccountManagement.exe` 同目录的 `appsettings.json` 后重启应用：

```json
{
  "ServerUrl": "https://accounts.example.com/",
  "UpdateUrl": "https://downloads.example.com/updates/windows/"
}
```

- `ServerUrl`：API 根地址，支持 HTTP/HTTPS 和端口，不包含 `/api` 路径。随程序提供的配置默认值为 `http://www.shjpnn.com:18082/`，其他部署者修改为自己的服务地址。
- `UpdateUrl`：更新文件目录，默认 `http://www.shjpnn.com/updates/windows/`；支持 HTTP/HTTPS，空字符串表示关闭自动和手动更新检查。其他部署者可修改为自己的更新目录。
- 若 `%LOCALAPPDATA%\AccountManagement\appsettings.json` 存在，则完整优先使用该文件。这适合安装版，升级替换程序目录时不会覆盖此文件；建议复制完整的两项配置再修改。
- API 地址优先级：登录页保存的 `server.txt` → 旧会话中保存的地址 → 配置文件中的 `ServerUrl`。已有用户升级后仍连接原地址；要切换服务器，在登录页“服务器设置”保存新地址，并用目标服务器帐号登录。
- 更新地址来自配置文件，与登录页保存的 API 地址独立。配置文件格式或地址非法时提示错误，不静默改连其他服务器。

构建前可修改 `desktop/AccountManagement/appsettings.json`，构建和发布会自动带上该文件。已经发布的更新包不要解压修改后重新压缩，否则会破坏包哈希验证；需要新版本重新打包。

更新源仍校验内置发布公钥，修改 URL 不会关闭签名校验。镜像同一发布者的签名产物可以直接使用；独立发布者须按[自动更新部署说明](../desktop/自动更新部署.md)准备自己的发布密钥、公钥和客户端，不能使用原作者私钥。

原作者服务器的现有用户可直接使用随包默认配置。其他部署者建议将自己的 API 和更新地址写入用户目录中的 `appsettings.json`，跨升级保留。已记住的 API 地址仍优先。若两个位置的配置文件均缺失，则安全回退到本机 API 并关闭更新；不要删除配置文件来切换服务器。

## 钉钉 H5

修改静态目录中的 `config.js`：

```javascript
window.ACCMGR_CONFIG = { apiBase: '/accmgr-api/' };
```

`apiBase` 为同域代理路径，必须以 `/` 开始和结束，支持 `/team/accounts-api/` 等多级路径。只允许字母、数字、下划线、连字符和路径分隔符，不接受跨域 URL、查询参数或 `..`。

在 Apache 的 `ProxyPass`、`ProxyPassReverse` 中同步修改路径及真实后端地址：

```apache
ProxyPass /accmgr-api/ http://127.0.0.1:18082/
ProxyPassReverse /accmgr-api/ http://127.0.0.1:18082/
```

页面域名由部署的站点决定，不写死在前端；钉钉应用首页填写自己的页面 URL。后端 `http.browserOrigin` 须与页面的协议、域名和端口一致，不带路径或末尾斜杠。保持同域代理，现有 CSP 和认证机制不变。

部署文件现在为 `index.html`、`app.js`、`styles.css`、`config.js` 四个。首次补齐 `config.js`；后续升级保留自己修改的配置，避免覆盖回默认路径。配置中不能放密码、令牌或钉钉密钥。

## 后端与数据库

在原 `config.properties` 中配置：

```properties
http.host=0.0.0.0
http.port=18082
http.browserOrigin=https://accounts.example.com
db.host=127.0.0.1
db.port=3306
db.name=account_management
```

旧配置未指定 `db.host`、`db.name` 时仍使用 `127.0.0.1` 和 `account_management`，无需修改已有部署。主机可为域名、IPv4 或带方括号的 IPv6；库名限字母、数字、下划线。当前 JDBC 使用非 TLS 连接，仅用于本机或可信私网，不要向公网暴露 MySQL。

修改地址后重启帐号管理后端。数据库必须事先存在并完成迁移，数据库用户授权需要对应主机与库名。仓库 SQL 默认使用 `account_management`，使用其他库名时要同步调整建库、授权及迁移脚本的 `USE`，不能仅改连接配置。原加密密钥保持不变。

钉钉官方 API/SDK URL、XAML 命名空间、文档链接和隔离测试地址不属于部署服务器配置，保持原样。
