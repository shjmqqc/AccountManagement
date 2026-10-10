# Windows 帐号管理客户端（核心功能版）

技术栈：C#、.NET 10、WPF、CommunityToolkit.Mvvm 8.4.2、WPF UI 4.3.0。

## 打开程序

解压 `AccountManagement-win-x64.zip` 的全部文件到同一目录，然后双击 `AccountManagement.exe`。
发行包携带 .NET Desktop Runtime，不需要同事另外安装 .NET，也不需要管理员权限。不能只复制 EXE。

目标为 Windows 10/11 x64；本机验证环境为 Windows 11，Windows 10 具体版本尚待用户电脑实测。微软的 .NET 10 官方 Windows 10 支持范围限于其支持表列出的版本，不能保证所有旧 Windows 10 构建。

## 连接当前服务器

后端当前只监听服务器 `127.0.0.1:18082`。先在 Windows PowerShell 建立 SSH 隧道（替换服务器 IP 和 SSH 用户）：

```powershell
ssh -N -o ExitOnForwardFailure=yes -L 127.0.0.1:18082:127.0.0.1:18082 SSH用户名@服务器IP
```

SSH 身份验证按现有服务器配置完成。该终端保持开启，没有输出是正常情况；Ctrl+C 关闭隧道。此命令不开放服务器 HTTP 或 MySQL 端口，不修改现有业务。

客户端服务地址填写 `http://127.0.0.1:18082`，使用已初始化的管理员用户名、登录密码登录；不是 Linux root 或 MySQL 用户密码。
若本机 18082 已占用，可将命令第一个端口改为 28082，客户端对应填写 `http://127.0.0.1:28082`，服务器端口仍为 18082。

配置正式 HTTPS 后，可填写 `https://你的域名`，届时不再需要 SSH 隧道。客户端拒绝公网明文 HTTP、含用户名密码的 URL、带路径/查询参数的服务地址，不绕过 HTTPS 证书验证，也不自动跟随重定向。

## 已实现

- 登录、退出、修改登录密码；处理强制改密、会话过期。
- 三栏布局：平台、帐号、详情；可拖动栏间分隔线。
- 全局搜索（按回车或点击搜索）、平台过滤、刷新。
- 新增/编辑/删除平台及帐号；删除前确认，非空平台禁止删除。
- 密码默认隐藏，可直接复制；切换帐号或窗口失去焦点后隐藏。
- 编辑帐号时默认不改密码；勾选修改后才提交，留空即清空。
- 打开平台网址、复制登录帐号、中文错误提示及操作状态。
- 登录令牌经 Windows DPAPI 当前用户保护后保存，按服务器的 7 天有效期恢复登录。文件位于 `%LOCALAPPDATA%\AccountManagement\session.bin`。

不会保存登录密码、离线平台帐号库或平台密码。用户主动复制的内容会进入系统剪贴板，遵循系统的剪贴板设置。
网络异常时不自动重放保存请求；若保存结果不明确，应先刷新确认。退出时若网络断开，客户端清除本机状态，但远端令牌可能保持有效直到过期。

## 当前范围

现有后端尚无邀请、管理员停用/重置/查看他人数据等管理接口，因此客户端没有展示不可用的管理入口。所有已接入操作都仅针对当前用户的数据。
微信小程序、自动升级、安装程序及代码签名未包含。此包用于当前阶段联调；建议先使用虚构数据。

## 开发与验证

安装 .NET 10.0.401 SDK，或使用项目 `.tools/dotnet` 下的独立 SDK。`global.json` 锁定 SDK 特性带，NuGet 锁文件记录依赖。

```powershell
# 在 desktop 目录
..\.tools\dotnet\dotnet.exe build AccountManagement/AccountManagement.csproj -c Release
.\test.ps1
.\publish.ps1
```

测试脚本先编译真实 Java 后端与隔离 H2 测试库，在本机 18083 端口启动一次性服务，执行客户端接口/视图模型验证并生成离屏界面预览，最后停止自己创建的服务。
仅用虚构测试帐号，不连接生产服务器、不修改系统剪贴板；需要事先运行 `backend/build.ps1` 准备 Java 依赖。测试数据目录为 `desktop/artifacts`，不提交 Git。

源码目录：Models 为接口数据，Services 为通信及登录状态，ViewModels 为列表和密码显示状态，Views 为界面及窗口交互。没有引入模块化框架或离线数据库。

已知验证边界：本机 H2 联调不替代服务器 MySQL 全流程、真实 SSH/HTTPS 接入或 Windows 10 实机测试。

官方资料：

- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/
- https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/
- https://wpfui.lepo.co/
- https://learn.microsoft.com/en-us/dotnet/core/install/windows
