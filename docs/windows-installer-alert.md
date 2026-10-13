# Windows 安装包告警与签名

2026-10-13，使用者反馈 Windows 1.1.8 安装程序 `Shjmqqc.AccountManagement-win-Setup.exe` 被 360 检出 `HEUR/QVM202.0B359.Malware.Gen`。截图指向安装包；目前尚未确认是否误报，也没有厂商复核结论。

当前安装程序由 Velopack 打包，未配置 Windows Authenticode 代码签名。自动更新使用的发布清单签名和包 SHA-256 校验用于验证来源与完整性，**不等同于 Windows 发布者签名或杀毒检测通过**。未签名应用更容易触发告警，参见 [Velopack 官方说明](https://docs.velopack.io/troubleshooting/faq)；这不能作为本次告警属于误报的证明。

遇到告警时，保留检测名称、文件路径、客户端版本和截图，暂停运行被拦截文件。可通过 [360 官方文件反馈](https://open.soft.360.cn/report.php)提交安装包复核；不要提交真实帐号数据、配置、密钥或数据库备份。不要以关闭杀毒、添加白名单或换用便携包作为已确认安全的替代方案。

可在 PowerShell 中核对本地文件哈希（将路径替换为实际位置；已隔离的文件无需为核对而恢复）：

```powershell
Get-FileHash -LiteralPath 'C:\下载目录\Shjmqqc.AccountManagement-win-Setup.exe' -Algorithm SHA256
```

本次 1.1.8 原始构建安装包的 SHA-256 为：

```text
9123A76E01777B18EA9221D1A0FF60CB7D95FF792C96310A8568AEECCF3B07CC
```

该哈希只适用于此次构建，不适用于其他版本或重新打包的文件。哈希一致只能确认与此构建产物相同，不能证明文件没有风险。后续接入代码签名有助于建立发布者身份，但不能保证消除所有杀毒告警。

