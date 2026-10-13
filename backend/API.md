# 帐号管理 API（含邀请、管理、分享与标签）

JSON 使用 UTF-8。除 POST /api/login、POST /api/register 和 GET /health 外均需 `Authorization: Bearer <token>`。
成功返回 HTTP 200，失败返回 `{"error":"错误码"}`。时间为 Unix 毫秒，记录 ID 为 UUID。
列表/详情不返回密码或密文；密码仅通过独立 POST 接口读取。本人接口从令牌确定所有者；管理员只能通过下列专用接口指定要查看的用户，不能借此修改他人的业务数据。

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

帐号新增/修改：同平台、同登录帐号、同帐号名称才返回 409 `duplicate_record`，不同名称允许使用相同登录帐号。后端持有所有者行锁后判重并写入；不支持绕过 API 直接写表。

有非空 q 时忽略 platform_id，搜索平台名称、帐号名称、登录帐号、备注，按字面包含匹配；不搜索密码。该阶段按每人约 100 条规模返回全部匹配结果，不提供分页。

错误状态：400 输入无效，401 登录无效/过期，403 必须改密或浏览器来源不允许，404 不存在或不属于本人，405 方法不支持，409 重复记录/非空平台，413 请求体过大，415 非 JSON 请求体，429 登录频率过高，500 服务端异常。

强制改密会话只能改密或退出；新密码不能与临时密码相同，否则返回 400 `new_password_must_differ`。改密成功后需要重新登录。

## 邀请注册

`POST /api/register` 无需登录。请求：

```json
{"invite_code":"管理员发放的43字符邀请码","username":"同事用户名","password":"至少6位"}
```

成功返回 `{"id":"用户UUID","login_required":true}`，随后调用登录接口。注册永远创建普通用户，不能指定管理员身份。用户名去除首尾空白，注册后不可修改。

邀请码只使用一次、创建后 7 天过期。不存在、过期、撤销或已使用均返回 400 `invalid_invitation`；用户名重复返回 409 `username_taken`，邀请码不会被消耗。校验失败和数据库失败也不会消耗邀请码。并发使用同一邀请码只允许一次成功。注册与登录共享每进程每分钟 60 次尝试限额，超限返回 429 `too_many_login_attempts`。

## 管理员接口

所有 `/api/admin/` 接口要求有效管理员会话。普通用户返回 403 `admin_required`；停用、过期或已撤销的会话返回 401。强制改密中的会话不可使用管理接口。

| 方法 | 路径 | 请求 / 返回 |
|---|---|---|
| POST | /api/admin/invitations | 无请求字段；返回 id、invite_code、expires_at |
| GET | /api/admin/invitations | 邀请元数据数组，含 id、created_by、created_at、expires_at、revoked_at、used_at、used_by、status |
| POST | /api/admin/invitations/{id}/revoke | 撤销未使用邀请；重复撤销成功，已使用返回 409 invitation_used |
| GET | /api/admin/users | 用户数组：id、username、role、active、must_change_password、created_at、account_count |
| POST | /api/admin/users/{userId}/status | 必填 JSON 布尔值 active：false 停用、true 恢复；返回 ok |
| POST | /api/admin/users/{userId}/reset-password | 无请求字段；返回 temporary_password、must_change_password:true |
| GET | /api/admin/users/{userId}/platforms | 指定用户的平台列表 |
| GET | /api/admin/users/{userId}/platforms/{id} | 指定用户的平台详情 |
| GET | /api/admin/users/{userId}/accounts | 指定用户的帐号列表；支持 q / platform_id，与本人搜索规则相同 |
| GET | /api/admin/users/{userId}/accounts/{id} | 指定用户的帐号详情，不含密码 |
| POST | /api/admin/users/{userId}/accounts/{id}/password | 必填 action：view 或 copy；返回该用户帐号密码 |
| GET | /api/admin/audit-logs | 操作记录数组，含 id、actor_id、target_user_id、target_id、action、outcome、created_at |

邀请列表 status 为 available / used / revoked / expired；未使用或未撤销的时间为 0，used_by 为 null。邀请码只在创建响应中返回一次，后端仅存摘要；复制并手动发放，遗失时撤销并创建新邀请。临时密码同样只在重置响应中返回一次，后端只存密码哈希。

邀请列表与审计列表支持 `limit=1..200`，默认 100，按 created_at 和 id 倒序。下一页用上一页最后一条 id 作为 `before_id`；无更多记录返回空数组。游标不存在返回 400 `invalid_cursor`。返回字段中的 active / must_change_password 是 0 或 1；登录响应的 must_change_password 是布尔值。

停用立即删除该用户全部会话；恢复不恢复旧会话。重置生成随机 20 字符临时密码、清除登录失败锁定、撤销全部会话并强制改密，不改变平台密码密文。对已停用用户重置不会恢复启用。管理员帐号禁止被停用或重置，返回 409 `admin_account_protected`；管理员自己的改密使用 `/api/password`。

他人数据查询和密码接口统一返回所属用户，便于客户端明确显示当前正在查看谁的数据：

```json
{"user":{"id":"用户UUID","username":"同事","role":"user","active":1},"data":{"password":"平台密码"}}
```

列表的 data 是数组，详情的 data 是对象。被停用用户的数据仍可供管理员查看。userId 与记录所属者必须一致，否则 404；管理路径的业务写操作返回 405，本人路径仍维持原来的所有者隔离。

审计记录邀请创建/撤销、平台和帐号读取、密码 view/copy、用户停用/恢复、密码重置的成功或失败。action 对应 invitation_create、invitation_revoke、platform_read、account_read、password_view、password_copy、user_disable、user_enable、password_reset；outcome 为 success 或 failure。日志仅保存固定操作类型、UUID 和时间，不包含邀请码、临时密码、平台密码、请求正文或异常内容。鉴权失败、无效路由/字段、用户与邀请列表查询、审计列表查询不记录为管理操作。view/copy 表示客户端声明的用途，无法证明操作系统剪贴板写入成功。

成功操作和审计在同一事务中提交，审计失败则不提交管理变更、不返回密码；业务失败先回滚，再记录 failure。客户端超时不能据此判断服务端一定未提交：邀请可在列表核对，密码重置响应遗失后可再次重置获得新的临时密码。

首次联调建议只使用虚构帐号和密码。通过接口创建平台和帐号后，退出并重新登录、重启该独立服务，再查看记录和读取密码，确认持久化与解密正常；不得重启旧业务来执行测试。

## 钉钉企业内部身份绑定与免登

仅配置启用后使用；本版数据库需执行至 006。`GET /api/dingtalk/config` 无需登录，仅返回 enabled、corp_id、client_id，绝不返回 AppSecret。

| 方法 | 路径 | 请求与行为 |
|---|---|---|
| POST | /api/dingtalk/login | 无需 Bearer；code 由钉钉 JSAPI 获取，后端换取身份；未绑定返回 bound=false，已绑定返回标准登录会话 |
| POST | /api/dingtalk/bind | 无需 Bearer；code、username、password，验证系统密码后绑定并返回会话；不会创建系统帐号 |
| GET | /api/dingtalk/binding | 需 Bearer；返回当前系统帐号在配置企业中的 bound 状态及 ding_name（未绑定/未同步为空字符串）；历史空姓名尝试补齐 |
| DELETE | /api/dingtalk/binding | 需 Bearer；password 再验证，解绑并撤销该帐号所有会话 |

公开登录/绑定及解绑共用登录限流，密码校验共用失败锁定。停用或锁定拒绝免登；强制改密返回 403 dingtalk_password_required，需走原密码登录和改密。绑定冲突返回 409 dingtalk_already_bound；上游不可用返回 502 dingtalk_unavailable；功能关闭返回 503 dingtalk_disabled。前端不得提交可信 userid 或任意企业配置。详细部署见 DINGTALK-UPGRADE.md。

## 单帐号分享（接口）

先应用 `004-sharing-tags.sql`。以下接口均要求有效会话，强制改密限制同其他业务接口。

| 方法 | 路径 | 请求 / 返回 |
|---|---|---|
| GET | /api/share-recipients?q=用户名或姓名 | 完整用户名精确匹配或配置企业的钉钉姓名片段匹配；仅启用且非本人，返回 id、username、ding_name 数组；空查询返回 400 |
| GET | /api/accounts/{id}/shares | 仅所有者可列出接收人：id、username、active、created_at |
| POST | /api/accounts/{id}/shares | `{ "recipient_id": "用户UUID" }`；添加只读授权，重复授权不重复创建 |
| DELETE | /api/accounts/{id}/shares/{recipientId} | 仅所有者可撤销；重复撤销成功 |
| GET | /api/shared/accounts | 分享给当前用户的帐号数组；支持 q、platform_id、tag_id |
| GET | /api/shared/accounts/{id} | 已授权帐号详情及 tags，不含密码 |
| POST | /api/shared/accounts/{id}/password | `{ "action": "view" }` 或 copy；返回 password |

共享列表在普通帐号字段之外包含 owner_id、owner_username、platform_url，供客户端显示来源。授权只指向原帐号，所有者修改后重新读取即可获得新内容。接收人不能编辑、删除、再次分享，也不能访问同平台其他帐号。管理员也不能代替他人授予分享权限。

无到期时间；撤销完成后后续读取返回 404，删除帐号自动清除授权。停用接收人使其会话失效；停用所有者期间其分享不可读取。授权记录保留，恢复启用后可继续使用（接收人需重新登录）。已显示或复制的内容不能收回。

审计 action 新增 share_grant、share_revoke、shared_password_view、shared_password_copy。授予/撤销时 actor_id 为所有者、target_user_id 为接收人；密码访问时 actor_id 为接收人、target_user_id 为所有者；target_id 为帐号。成功操作与审计同事务，密码访问审计失败时不返回密码。通过路由及字段校验的业务失败记录 failure；未经所有权校验的分享管理请求不记录。并发事务冲突可能返回 409 concurrent_change_retry，刷新后重试。

## 彩色标签

| 方法 | 路径 | 请求 / 返回 |
|---|---|---|
| GET | /api/tags | 本人的可复用标签目录，数组元素为 id、name、color、account_count（使用帐号数） |
| POST | /api/tags | `{ "name": "测试帐号", "color": "blue" }`；返回标签对象；同用户同名返回原标签及原颜色 |
| PUT | /api/tags/{id} | 所有者修改 name、color；同名其他标签返回 409 tag_name_taken |
| DELETE | /api/tags/{id} | 所有者删除标签并解除全部关联，保留帐号及分享授权 |

名称去除首尾空白后不能为空，输入最多 40 个 Unicode 字符；颜色限定 blue、green、orange、red、purple、gray。标签目录按用户隔离，不能给帐号绑定他人的标签，不能修改或删除他人的标签。改名/改色后所有关联帐号及只读接收人刷新可见，不合并同名标签。

新增/编辑帐号可传 `tag_ids: ["标签UUID", "标签UUID"]`，每帐号最多 20 个。传空数组移除该帐号的所有标签；不传则保留已有标签，兼容旧客户端。绑定与帐号保存同事务，无效标签导致整次保存回滚。取消帐号表单不删除已独立创建的目录标签。

本人、管理员查看、收到的帐号列表和详情均包含 `tags: [{"id":"…","name":"测试帐号","color":"blue"}]`，无标签为空数组。列表 `tag_id` 为单标签筛选，与 q/platform_id 的结果取交集；有非空 q 仍忽略 platform_id。关键词搜索不包含标签文字。收到的标签只展示和筛选，接收人不能修改。

## Excel 逐行导入

`POST /api/accounts/import`，需 Bearer；JSON 字段：`platform_name`、`login` 必填，`platform_url`、`name`、`password`、`notes` 可选，`tag_names` 为最多 20 个标签名称的数组。

同用户下查找平台，存在则保留原地址，不存在则创建；同平台/登录帐号/帐号名称已有记录返回 `{ "status": "skipped" }`，不修改任何已有数据。新建返回 `{ "status": "created", "id": "…" }`。标签同名复用原色，新标签默认蓝色。行内全部写入同一事务；失败时平台、标签、帐号一起回滚。所有者来自会话，客户端不能指定他人帐号库；沿用 006 的所有者行锁保证并发判重。

Windows 先本地解析与预览 xlsx，再逐行调用此接口。Excel 文件不上传服务器；接口不接受整份文件或批量数组。
