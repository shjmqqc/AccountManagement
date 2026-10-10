# 核心 API（第一阶段）

JSON 使用 UTF-8。除 POST /api/login 和 GET /health 外均需 `Authorization: Bearer <token>`。
成功返回 HTTP 200，失败返回 `{"error":"错误码"}`。时间为 Unix 毫秒，记录 ID 为 UUID。
列表/详情不返回密码或密文；密码仅通过独立 POST 接口读取。服务端从令牌确定所有者，不接受客户端指定 owner_id。

| 方法 | 路径 | 请求/行为 |
|---|---|---|
| GET | /health | 进程存活检查，不保证每次请求时数据库可用 |
| POST | /api/login | username、password；返回 token、expires_at、must_change_password |
| POST | /api/logout | 注销当前令牌 |
| GET | /api/me | 返回 id、username、role |
| POST | /api/password | old_password、new_password；所有旧令牌立即失效 |
| GET | /api/platforms | 本人平台数组，含 account_count |
| POST | /api/platforms | name 必填、url 可选；返回 id |
| GET | /api/platforms/{id} | 本人平台详情 |
| PUT | /api/platforms/{id} | name 必填、url 可选；不填 url 则清空 |
| DELETE | /api/platforms/{id} | 非空平台返回 409；界面须先确认 |
| GET | /api/accounts | 本人帐号数组；可用 q 全局搜索，或 platform_id 过滤 |
| POST | /api/accounts | platform_id、login 必填；name、password、notes 可选；返回 id |
| GET | /api/accounts/{id} | 本人帐号详情，不包含密码 |
| PUT | /api/accounts/{id} | platform_id、login 必填；name、notes 不填则清空；password 不传则保留、传空字符串则清空 |
| DELETE | /api/accounts/{id} | 删除本人帐号；界面须先确认 |
| POST | /api/accounts/{id}/password | 返回 password，供显示/复制 |

有非空 q 时忽略 platform_id，搜索平台名称、帐号名称、登录帐号、备注，按字面包含匹配；不搜索密码。该阶段按每人约 100 条规模返回全部匹配结果，不提供分页。

错误状态：400 输入无效，401 登录无效/过期，403 必须改密或浏览器来源不允许，404 不存在或不属于本人，405 方法不支持，409 重复记录/非空平台，413 请求体过大，415 非 JSON 请求体，429 登录频率过高，500 服务端异常。

强制改密会话只能改密或退出。重置密码及管理员操作接口留待下一阶段；禁止客户端通过直接数据库写入来代替这些接口。

首次联调建议只使用虚构帐号和密码。通过接口创建平台和帐号后，退出并重新登录、重启该独立服务，再查看记录和读取密码，确认持久化与解密正常；不得重启旧业务来执行测试。
