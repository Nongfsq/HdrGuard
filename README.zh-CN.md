<p align="center">
  <img src="assets/HdrGuard.svg" alt="HdrGuard 图标" width="128" height="128">
</p>

<h1 align="center">HdrGuard</h1>

<p align="center">
  一个安静运行的 Windows 托盘工具：当 RustDesk 远程控制时自动关闭 HDR，
  让被控机器的画面更容易看清；远程会话结束后再恢复 HDR。
</p>

<p align="center">
  <a href="README.md">English</a>
  ·
  <a href="https://github.com/Nongfsq/HdrGuard/releases/latest">下载最新版</a>
  ·
  <a href="PRIVACY.md">隐私说明</a>
  ·
  <a href="CONTRIBUTING.md">贡献规则</a>
</p>

## 它解决什么问题

在开启 Windows HDR 的机器上使用 RustDesk 远程控制时，画面有时会发白、
过亮、偏灰，或者在另一台设备上很难阅读。HdrGuard 专门处理这个问题。

它运行在被控的 Windows 机器上，检测到 RustDesk 远程控制会话后，自动把支持
HDR 的显示器切到 SDR；会话结束后，再按配置的延迟恢复 HDR。

HdrGuard 不做云同步，不上传日志，不连接任何项目服务。它只在本机工作。

## 系统要求

- Windows 10 或 Windows 11。
- 被控机器已安装 RustDesk。
- 至少有一个支持 HDR 的显示器。

发布包是自包含的 `win-x64` 构建。直接下载 Release ZIP 即可运行，不需要单独安装
.NET 运行时。

## 快速开始

1. 从 [latest release](https://github.com/Nongfsq/HdrGuard/releases/latest)
   下载 `HdrGuard-win-x64.zip`。
2. 解压到你自己控制的文件夹。
3. 运行 `HdrGuard.exe`。

当前版本未做代码签名，Windows SmartScreen 可能提示未知发布者。如果你需要确认文件
完整性，请同时校验 Release 中提供的 SHA256 文件。

## 安装到开机启动

在解压后的 release 文件夹中运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1 -Start
```

默认情况下，安装脚本会保留当前解压目录作为程序目录，并创建开机启动快捷方式。
如果你想安装到指定目录：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1 -InstallDir C:\Tools\HdrGuard -Start
```

开机启动快捷方式位置：

```text
%AppData%\Microsoft\Windows\Start Menu\Programs\Startup\HdrGuard.lnk
```

卸载程序但保留配置、状态和日志：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\uninstall.ps1
```

如果也要删除程序同目录下的 `data` 文件夹：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\uninstall.ps1 -RemoveUserData
```

## 托盘菜单

- `Enabled`：暂停或恢复自动守护。
- `Turn HDR on` / `Turn HDR off`：手动开启或关闭 HDR。
- `Restore HDR now`：恢复由 HdrGuard 管理的显示器 HDR 状态。
- `Open config` / `Open log`：打开本地配置和日志。
- `Exit`：退出托盘程序。

## 手动命令

这些命令只执行一次，不会启动托盘程序：

```powershell
HdrGuard.exe --hdr-on
HdrGuard.exe --hdr-off
```

`--hdr-on` 会同时清理 HdrGuard 记录的守护状态，避免一个等待中的自动恢复动作和你的
手动恢复互相冲突。

## 配置

HdrGuard 默认是便携式的。首次运行时，它会在 `HdrGuard.exe` 同目录创建 `data`
文件夹：

```text
.\data\config.json
.\data\state.json
.\data\HdrGuard.log
```

如果你从旧版本升级，HdrGuard 会把旧位置 `%AppData%\HdrGuard` 中缺失的文件复制到
`.\data`，但不会覆盖 `.\data` 中已经存在的文件。

公开的默认配置样例位于 `src/HdrGuard/config.example.json`。常用字段如下：

| 字段 | 作用 |
| --- | --- |
| `rustDesk.logPaths` | 要监听的 RustDesk connection-manager 日志路径。 |
| `rustDesk.pollSeconds` | 日志轮询间隔。 |
| `rules[].restoreAfterMinutes` | RustDesk 断开后多久恢复 HDR。 |
| `rules[].maxDisabledMinutes` | 漏掉断开事件时的最长关闭 HDR 时间。 |
| `rustDesk.enableNetworkFallback` | 可选网络兜底检测，默认关闭。 |

检测逻辑优先依赖 RustDesk 日志。额外配置的 `connectPatterns` 和
`disconnectPatterns` 会扩展内置规则，不会替换内置规则。

`enableNetworkFallback` 默认关闭，因为 RustDesk 可能保留后台网络连接，而这些连接不
一定代表正在进行远程控制。

## 故障排查

- 如果远程控制时托盘状态仍然 idle，打开日志，检查 `config.json` 中的 RustDesk
  connection-manager 日志路径。
- 如果恢复 HDR 时显示 0 个显示器，可能是驱动更新、显示器重连或显示拓扑变化导致
  显示器 ID 变了。可以运行 `HdrGuard.exe --hdr-on` 作为恢复命令。
- 如果 HDR 恢复得太早或太晚，调整 `restoreAfterMinutes`。
- 如果 HdrGuard 没有捕捉到断开事件，`maxDisabledMinutes` 会作为兜底，在达到上限
  后恢复 HDR。

## 隐私

HdrGuard 是本地工具。它只读取本机 RustDesk 日志来判断远程会话连接和断开，不发送
遥测，不上传日志，也不会连接任何项目服务。

在 GitHub issue 中分享日志前，请先阅读 `PRIVACY.md` 并脱敏。

## 从源码构建

安装 .NET 10 SDK 后运行：

```powershell
dotnet test .\tests\HdrGuard.Tests\HdrGuard.Tests.csproj
dotnet publish .\src\HdrGuard\HdrGuard.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

构建 release ZIP：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\package-release.ps1
```

## 贡献

提交修改前请先阅读 `CONTRIBUTING.md`。这是公开仓库，严禁提交个人配置、日志、本机
路径、密钥、内部进度文档或 AI 工作记录。

## 许可证

Apache-2.0。详见 `LICENSE` 和 `NOTICE`。
