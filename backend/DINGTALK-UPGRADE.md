# 钉钉企业内部 H5 绑定与免登升级

本次新增一张绑定表，更新帐号管理后端和 H5 文件。Windows 客户端无需更新；不要修改另一项 Spring Boot 服务、原加密密钥或初始化管理员。

## 升级顺序

1. 使用之前已验证的方式备份数据库、现有 JAR 和配置，单独安全保留原密钥。停止帐号管理服务。
2. 确认已执行 004，使用具备 DDL 权限的数据库帐号执行 `005-dingtalk.sql` 一次。脚本仅新增 `am_dingtalk_bindings`，不修改已有帐号数据。MySQL 5.5 DDL 非事务性，报错先检查，不使用 `--force`。
3. 替换 `account-management.jar`，保留原 lib、配置和密钥。
4. 在原配置中填写以下项目，再按原方式启动：

```properties
dingtalk.enabled=true
dingtalk.corpId=企业CorpId
dingtalk.clientId=应用ClientID或原AppKey
dingtalk.clientSecret=应用ClientSecret或原AppSecret
```

配置文件仅服务进程和管理人员可读；AppSecret 不进入 H5、Git、公开目录或聊天。暂不启用时保留 `dingtalk.enabled=false`，旧服务登录不受影响；关闭免登配置不自动撤销已有应用会话，需要停用用户或重置密码时使用原管理功能。

5. 更新 H5 发布 ZIP 中的三个文件。继续使用 Apache 同域代理与精确 `http.browserOrigin` 配置，不放开任意来源。
6. 在钉钉后台设置应用首页、安全域名及可见范围，核对应用调用相关免登接口的权限（以当前后台提示为准）。本实现仅换取 userid，不拉取通讯录、手机号或姓名。首页 URL 的 corpid/userid 参数不作为身份凭据，组织固定使用服务端配置。

真实帐号登录与绑定应使用 HTTPS；服务器需要能够通过有效 HTTPS 证书连接 `oapi.dingtalk.com`，浏览器需能加载固定版本官方 JSAPI SDK。旧 Java 8/系统环境必须实测 TLS 和 CA 信任；失败时回退密码登录，不关闭证书校验。

## 行为与验收

- 仅在钉钉容器内尝试免登，每次打开通过新的钉钉授权码确认身份，不直接复用另一个钉钉用户遗留的网页令牌。
- 首次未绑定：输入现有帐号密码、勾选绑定并确认。新用户先通过现有邀请注册流程获得系统帐号，不自动注册、不按姓名匹配。
- 一个企业的一个钉钉 userid 只能绑定一个系统帐号，同一帐号在该企业只能绑定一个 userid。已有不同绑定必须先解绑，不能覆盖。
- 后端验证身份、绑定归属、停用、锁定和强制改密；管理员重置后，先用临时密码登录改密。绑定不会改变用户角色或原数据。
- 点击退出后，本页面会话（含刷新）暂停自动登录，仍可主动点击钉钉登录；新打开的页面可能重新触发免登，取决于 WebView 会话生命周期。
- 解绑需要当前系统密码，撤销该帐号所有现有登录会话；重新绑定必须再次验证密码。绑定/解绑成功记录审计，不记录授权码或密码。
- 普通浏览器继续密码登录；SDK加载、授权码、上游网络失败均显示提示并保留密码入口。钉钉身份只能由服务器向钉钉验证，前端传入 userid/角色无效。

验收请在 Android 和 iPhone 钉钉内分别测试：未绑定、首次绑定、再次打开免登、主动退出与刷新、解绑、错误密码、用户停用、管理员重置后强制改密、断网或 SDK 不可用。自动化使用模拟钉钉返回验证本地权限，不能替代真实 CorpId/AppKey/AppSecret 联调。

参考：[官方 requestAuthCode](https://open.dingtalk.com/tools/explorer/jsapi?id=11723)、[官方 H5 免登示例](https://github.com/open-dingtalk/h5app-auth-demo)。当前前端调用 `dd.requestAuthCode`，手机钉钉需支持该接口（官方列出的 Android/iOS 最低版本 7.0.45），不支持时提示使用密码登录。

## 回滚

先将 `dingtalk.enabled=false` 并重启，可停用新免登入口；保留绑定表以便恢复。也可恢复旧 JAR 和 H5 文件，不删除绑定表或密钥，不重新执行旧建表脚本。
