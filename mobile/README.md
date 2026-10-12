# 钉钉企业内部 H5 微应用（第一版）

纯静态 HTML/CSS/JavaScript，生产服务器无需安装 Node.js，也无需前端构建。使用现有帐号密码登录，暂不集成钉钉免登，所以不需要 AppKey/AppSecret。

## 功能范围

- 我的帐号、分享给我、分享给别人；关键词搜索、平台及标签筛选。
- 查看详情、显示或复制帐号密码；切到后台、返回列表时隐藏密码。
- 按完整用户名查找接收人，确认分享，查看已有接收人及撤销。
- 修改登录密码、强制修改临时密码、退出和会话失效处理。
- 第一版暂不含帐号/平台编辑、邀请注册、管理员操作或批量分享。

令牌仅存于当前页面会话的 sessionStorage，不保存登录密码或帐号库到本地。刷新可恢复当前会话，但关闭钉钉页面后的保留行为依赖 WebView，不承诺与 Windows 一样保留 7 天。密码只在用户明确显示/复制时请求；复制失败会选中文本提示手动复制，不显示虚假的成功提示。HTTP 下剪贴板能力可能受限。

## 服务器部署

先更新本项目配套后端 JAR，再部署网页，无需数据库变更。保留原配置和加密密钥。后端现在支持 `http.browserOrigin`：留空仍拒绝浏览器 Origin；设置为精确的页面来源，例如 `https://www.shjpnn.com`，不加路径或末尾斜杠。HTTP 联调时必须对应 `http://www.shjpnn.com`。不使用通配符，不删除浏览器 Origin 请求头。

1. 备份现有帐号管理 JAR，按原方式停止此进程、替换 JAR。
2. 在原 `config.properties` 中增加 `http.browserOrigin=实际页面来源`，按原方式启动。
3. 将发布 ZIP 中的 `index.html`、`app.js`、`styles.css` 放至 `/opt/acc_mgr/mobile/`，Apache 用户需要目录遍历和文件读取权限。
4. 将 `apache2-snippet.conf` 内容合并到 **www.shjpnn.com 实际生效的 VirtualHost 内**，不替换原虚拟主机，不影响现有博客或更新目录。
5. 确认 Apache 的 `alias`、`proxy`、`proxy_http` 模块可用。只有 `apache2ctl configtest` 为 Syntax OK 后才 reload。
6. 浏览器访问 `/accmgr-mobile/`，验证登录、密码读取、授权和撤销。登录密码与真实平台密码应通过 HTTPS 传输；当前服务器 HTTPS 尚未完成，HTTP 仅使用测试帐号联调。

API 固定走同域 `/accmgr-api/`，Apache 转发到本机 18082，不增加跨域访问配置；前端不允许任意指定服务器。更新页面时一起替换三个文件。Apache 示例禁用缓存，后端 API 同样返回 no-store。

## 钉钉后台

创建“企业内部应用 → H5 微应用”，应用名建议“帐号管理”。将移动端首页设为 `https://www.shjpnn.com/accmgr-mobile/`（以实际可访问的协议为准），PC 首页可使用相同地址，设置可见范围为需要使用的成员，再发布到工作台。具体字段以当前开发者后台为准。

第一版通过工作台入口打开页面，再登录现有帐号；不读取钉钉通讯录、不申请免登权限。钉钉可见范围控制入口，系统内帐号与分享权限仍由后端验证，并不等于钉钉组织身份绑定。不要把 AppSecret 写进网页。

参考：[钉钉官方 H5 企业内部应用示例](https://github.com/open-dingtalk/h5app-corp-quickstart)、[官方免登示例（后续阶段）](https://github.com/open-dingtalk/h5app-auth-demo)。上线前需在 Android 和 iPhone 的钉钉内分别验收：登录、筛选、复制兼容、切后台隐藏密码、分享及撤销；本机浏览器验证不能代替真机验收。

## 本地验证

使用现有 Java 测试夹具（一次性 H2 内存数据库），允许来源 `http://127.0.0.1:18084`；运行 `node mobile/dev-server.mjs` 提供本地静态页面和同域代理，再运行 `node mobile/check-api.mjs` 创建测试数据并验证接口。开发服务器仅绑定 127.0.0.1，生产环境不部署此脚本或测试帐号。

发布：`pwsh -File mobile/publish.ps1`，仅打包三个静态文件，不包含测试脚本、密钥或真实数据。
