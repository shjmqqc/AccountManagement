# 钉钉企业内部 H5 微应用（第一版）

纯静态 HTML/CSS/JavaScript，生产服务器无需安装 Node.js，也无需前端构建。支持原帐号密码登录；可选启用钉钉绑定与免登，首次验证密码绑定，以后从钉钉进入自动登录。AppSecret 仅保存在服务器，具体升级步骤见 [钉钉免登升级](../backend/DINGTALK-UPGRADE.md)。

## 功能范围

- 首次密码验证绑定钉钉身份、之后免登、密码登录回退、密码确认解绑；退出后暂停当前页面会话的自动登录。
- 我的帐号、分享给我、分享给别人；关键词搜索、平台及标签筛选。
- 查看详情、显示或复制帐号密码；切到后台、返回列表时隐藏密码。
- 按完整用户名查找接收人，确认分享，查看已有接收人及撤销。
- 修改登录密码、强制修改临时密码、退出和会话失效处理。
- 第一版暂不含帐号/平台编辑、邀请注册、管理员操作或批量分享。

令牌仅存于当前页面会话的 sessionStorage，不保存登录密码或帐号库到本地。刷新可恢复当前会话，但关闭钉钉页面后的保留行为依赖 WebView，不承诺与 Windows 一样保留 7 天。密码只在用户明确显示/复制时请求；复制失败会选中文本提示手动复制，不显示虚假的成功提示。HTTP 下剪贴板能力可能受限。

## 服务器部署

已有分享/标签功能的服务器，仅接入密码登录 H5 无需数据库变更；启用钉钉免登需先执行一次 `005-dingtalk.sql`，按[免登升级说明](../backend/DINGTALK-UPGRADE.md)配置，再更新后端和网页。保留原配置和加密密钥。`http.browserOrigin` 留空仍拒绝浏览器 Origin；设置为精确的页面来源，例如 `https://www.shjpnn.com`，不加路径或末尾斜杠。HTTP 联调时必须对应 `http://www.shjpnn.com`。不使用通配符，不删除浏览器 Origin 请求头。

1. 备份现有帐号管理 JAR，按原方式停止此进程、替换 JAR。
2. 在原 `config.properties` 中增加 `http.browserOrigin=实际页面来源`，按原方式启动。
3. 将发布 ZIP 中的 `index.html`、`app.js`、`styles.css` 放至 `/opt/acc_mgr/mobile/`，Apache 用户需要目录遍历和文件读取权限。
4. 将 `apache2-snippet.conf` 内容合并到 **www.shjpnn.com 实际生效的 VirtualHost 内**，不替换原虚拟主机，不影响现有博客或更新目录。
5. 确认 Apache 的 `alias`、`proxy`、`proxy_http` 模块可用。只有 `apache2ctl configtest` 为 Syntax OK 后才 reload。
6. 浏览器访问 `/accmgr-mobile/`，验证登录、密码读取、授权和撤销；免登和绑定需从钉钉内打开测试。登录密码与真实平台密码应通过 HTTPS 传输；HTTP 仅用于测试帐号联调。

API 固定走同域 `/accmgr-api/`，Apache 转发到本机 18082，不增加跨域访问配置；前端不允许任意指定服务器。更新页面时一起替换三个文件。Apache 示例禁用缓存，后端 API 同样返回 no-store。

## 钉钉后台

创建“企业内部应用 → H5 微应用”，应用名建议“帐号管理”。将移动端首页设为 `https://www.shjpnn.com/accmgr-mobile/`（以实际可访问的协议为准），PC 首页可使用相同地址，设置可见范围为需要使用的成员，再发布到工作台。具体字段以当前开发者后台为准。

工作台打开后，未启用免登时使用原帐号密码；启用后通过钉钉授权码验证身份，未绑定者可勾选绑定并验证密码。系统内帐号与分享权限仍由后端验证，钉钉可见范围不代替系统授权。支持密码确认解绑，解绑撤销所有现有会话；主动退出会暂停当前页面会话的自动登录。不要把 AppSecret 写进网页。

参考：[钉钉官方 H5 企业内部应用示例](https://github.com/open-dingtalk/h5app-corp-quickstart)、[官方免登示例](https://github.com/open-dingtalk/h5app-auth-demo)。上线前需在 Android 和 iPhone 的钉钉内分别验收：登录、绑定与免登、筛选、复制兼容、切后台隐藏密码、分享及撤销；本机浏览器验证不能代替真机验收。

## 本地验证

现有部署的 H5 功能与钉钉绑定免登已由使用者反馈运行正常。该反馈不代表所有 Android/iPhone 与钉钉版本均已覆盖，其他环境仍按上述流程验收。

使用现有 Java 测试夹具（一次性 H2 内存数据库），允许来源 `http://127.0.0.1:18084`；运行 `node mobile/dev-server.mjs` 提供本地静态页面和同域代理，再运行 `node mobile/check-api.mjs` 创建测试数据并验证接口。开发服务器仅绑定 127.0.0.1，生产环境不部署此脚本或测试帐号。

发布：`pwsh -File mobile/publish.ps1`，仅打包三个静态文件，不包含测试脚本、密钥或真实数据。
