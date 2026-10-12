# 第三方依赖说明

仓库根目录的 MIT License 适用于 AccountManagement 自身代码，不替代第三方组件的许可证。构建脚本会下载依赖；自包含客户端还会携带 .NET 运行时。

| 组件 | 当前版本 | 用途 / 上游 |
|---|---|---|
| .NET / WPF | .NET 10；SDK 固定策略见 desktop/global.json | Windows 运行时与界面；[dotnet](https://github.com/dotnet) |
| CommunityToolkit.Mvvm | 8.4.2 | MVVM；[CommunityToolkit/dotnet](https://github.com/CommunityToolkit/dotnet) |
| WPF UI / WPF UI Abstractions | 4.3.0 | Windows 控件；[lepoco/wpfui](https://github.com/lepoco/wpfui) |
| Velopack | 1.2.161 | 安装与更新（MIT）；[velopack/velopack](https://github.com/velopack/velopack)，打包工具固定同版本 |
| Gson | 2.13.2 | JSON；[google/gson](https://github.com/google/gson) |
| MySQL Connector/J | 5.1.49 | JDBC 驱动；[上游版本归档与许可说明](https://downloads.mysql.com/archives/c-j/) |
| H2 | 2.2.224 | 仅测试使用，不放入后端运行包；[h2database](https://github.com/h2database/h2database) |

MySQL 官方将其开源 Connector/J 下载标明为 GPL 授权，不能把该驱动标记为本项目的 MIT 代码。分发包含该驱动的部署包时，应同时遵守对应版本的上游许可证及适用条款；本文件不替代上游许可证原文。

Windows 发布脚本会将其收集到的依赖许可证与运行时声明放入 `licenses/`。第三方完整版权、许可、例外及其他声明应以对应版本的上游发行物为准；此表是依赖索引，不表示整份二进制包均采用 MIT。
