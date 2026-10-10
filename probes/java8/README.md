# Java 8 运行兼容性探针

这是部署兼容性验证程序，不是正式后端，不包含登录、数据库或密码保存功能。
使用 JDK 自带 HTTP 服务，不下载依赖，不修改系统 Java 配置。
仅监听本机回环地址，不向互联网暴露接口。默认端口 18081，可通过首个参数指定。

## Windows 本机编译和运行

在本目录执行 PowerShell：

```powershell
New-Item -ItemType Directory -Force build | Out-Null
& 'C:\Program Files\Java\jdk1.8.0_192\bin\javac.exe' -encoding UTF-8 -d build RuntimeProbe.java
& 'C:\Program Files\Java\jdk1.8.0_192\bin\jar.exe' cfe build/runtime-probe.jar RuntimeProbe -C build RuntimeProbe.class
& 'C:\Program Files\Java\jdk1.8.0_192\bin\java.exe' -Xms16m -Xmx64m -jar build/runtime-probe.jar
```

另一个终端访问 `http://127.0.0.1:18081/health`，应返回 HTTP 200 和 `status: ok`。
其他路径返回 404，健康检查非 GET 请求返回 405。使用 Ctrl+C 停止。

## 后续 Linux 验证（尚未执行）

确认独立目录及空闲端口后，上传生成的 JAR，再手动执行：

```bash
java -Xms16m -Xmx64m -jar runtime-probe.jar 18081
```

从服务器本机检查健康接口，结束后停止探针。此阶段不配置自启动、不接入现有业务、不连接数据库。
64 MB 仅为 Java 堆上限，不代表进程总内存上限。

本机通过不等于服务器验证通过。数据库版本、数据库连接、HTTPS、实际资源占用仍需独立验证。
探针不决定正式后端框架；后续选型需要结合部署验证结果。
