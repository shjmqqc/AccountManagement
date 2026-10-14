# 1.1.9

- Windows 帐号详情新增“整体复制”，与 H5 一致，按平台名称、地址、帐号、密码四行复制，沿用密码读取权限和复制审计。

- 默认 API 地址和自动更新目录改为 appsettings.json 配置，随包默认值保留作者服务器，其他部署者可自行修改。
- 支持用户目录配置优先于安装目录，避免应用升级覆盖部署设置；登录页已保存的 API 地址继续保留。
- UpdateUrl 留空关闭更新检查，配置格式或地址错误时提示修正。
- 配套 H5 新增 config.js 配置同域 API 路径；后端增加 db.host、db.name，旧配置保持原默认值。

原作者服务器用户可直接使用默认配置；其他部署者请将自己的 ServerUrl 和 UpdateUrl 保存到 %LOCALAPPDATA%\AccountManagement\appsettings.json，跨升级保留。H5 升级需新增 config.js，保留自己的代理路径。本版无需新增数据库迁移，不更换原加密密钥。
