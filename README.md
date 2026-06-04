<p align="center">
  <img src="assets/HdrGuard.svg" alt="HdrGuard logo" width="128" height="128">
</p>

<h1 align="center">HdrGuard</h1>

<p align="center">
  A quiet Windows tray utility that keeps RustDesk remote sessions readable when HDR
  makes the controlled machine look washed out, over-bright, or hard to use.
</p>

<p align="center">
  <a href="README.zh-CN.md">简体中文</a>
  ·
  <a href="https://github.com/Nongfsq/HdrGuard/releases/latest">Download latest release</a>
  ·
  <a href="PRIVACY.md">Privacy</a>
  ·
  <a href="CONTRIBUTING.md">Contributing</a>
</p>

## What It Does

HdrGuard runs on the Windows machine being controlled through RustDesk. When a
RustDesk remote-control session is detected, it turns HDR off for supported displays.
After the session ends, it restores HDR after a short delay.

It is designed for a narrow, practical problem: Windows HDR can make remote sessions
look wrong, especially when the viewer is on a different display, operating system, or
color pipeline. HdrGuard stays local, watches local RustDesk signals, and gets out of
the way.

## Requirements

- Windows 10 or Windows 11.
- RustDesk installed on the Windows machine being controlled.
- At least one HDR-capable display.

Release ZIPs are self-contained `win-x64` builds. You do not need to install the .NET
runtime for the published ZIP.

## Quick Start

1. Download `HdrGuard-win-x64.zip` from the
   [latest release](https://github.com/Nongfsq/HdrGuard/releases/latest).
2. Extract it to a folder you control.
3. Run `HdrGuard.exe`.

Windows SmartScreen may warn because current releases are not code-signed. Verify the
release SHA256 file if you need stronger provenance.

## Install At Login

From the extracted release folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1 -Start
```

By default, the installer keeps HdrGuard in the extracted folder and creates a Startup
shortcut. To install somewhere else:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1 -InstallDir C:\Tools\HdrGuard -Start
```

The Startup shortcut is created at:

```text
%AppData%\Microsoft\Windows\Start Menu\Programs\Startup\HdrGuard.lnk
```

To uninstall the app while keeping your app-local config, state, and log files:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\uninstall.ps1
```

To also remove the app-local `data` folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\uninstall.ps1 -RemoveUserData
```

## Tray Menu

- `Enabled` pauses or resumes automatic guarding.
- `Turn HDR on` and `Turn HDR off` run manual HDR commands.
- `Restore HDR now` restores only displays currently guarded by HdrGuard.
- `Open config` and `Open log` open local runtime files.
- `Exit` closes the tray app.

## Manual Commands

These commands run once and exit. They do not start the tray app.

```powershell
HdrGuard.exe --hdr-on
HdrGuard.exe --hdr-off
```

`--hdr-on` also clears guarded state, so a pending restore will not fight a manual
recovery action.

## Configuration

HdrGuard is portable by default. On first run, it creates a `data` folder next to
`HdrGuard.exe`:

```text
.\data\config.json
.\data\state.json
.\data\HdrGuard.log
```

If you upgrade from an older release, HdrGuard copies missing files from the legacy
`%AppData%\HdrGuard` directory into `.\data` once. It does not overwrite files that
already exist in `.\data`.

The public default configuration is stored in
`src/HdrGuard/config.example.json`. The most commonly adjusted fields are:

| Field | Purpose |
| --- | --- |
| `rustDesk.logPaths` | RustDesk connection-manager log paths to watch. |
| `rustDesk.pollSeconds` | Polling interval for log detection. |
| `rules[].restoreAfterMinutes` | Delay before restoring HDR after disconnect. |
| `rules[].maxDisabledMinutes` | Safety cap for restoring HDR if a disconnect is missed. |
| `rustDesk.enableNetworkFallback` | Optional network fallback, disabled by default. |

Detection is log-first. Additional `connectPatterns` and `disconnectPatterns` extend
the built-in RustDesk regexes; they do not replace them.

`enableNetworkFallback` is off by default because RustDesk can keep background network
connections open that are not active remote-control sessions.

## Troubleshooting

- If the tray stays idle during a RustDesk session, open the log and verify the
  RustDesk connection-manager log path in `config.json`.
- If HDR is restored for 0 displays, display identifiers may have changed after a
  driver update, monitor reconnect, or topology change. Run `HdrGuard.exe --hdr-on`
  as the recovery command.
- If HDR returns too soon or too late, adjust `restoreAfterMinutes`.
- If HdrGuard misses a disconnect, `maxDisabledMinutes` is the safety cap that
  restores HDR even without a disconnect log event.

## Privacy

HdrGuard is local-only software. It reads local RustDesk logs only to infer session
connect/disconnect state. It does not send telemetry, upload logs, or contact a
service.

See `PRIVACY.md` before sharing logs in issues.

## Build From Source

Install the .NET 10 SDK, then run:

```powershell
dotnet test .\tests\HdrGuard.Tests\HdrGuard.Tests.csproj
dotnet publish .\src\HdrGuard\HdrGuard.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

To build a release ZIP:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\package-release.ps1
```

## Contributing

Read `CONTRIBUTING.md` before sending changes. This public repository has strict
rules for avoiding personal configs, logs, machine paths, secrets, and private working
notes.

## License

Apache-2.0. See `LICENSE` and `NOTICE`.
