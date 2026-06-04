# HdrGuard

HdrGuard is a small Windows tray utility that turns display HDR off while a RustDesk
remote-control session is active, then restores HDR after the session ends.

It is meant for Windows machines that look wrong or hard to use through RustDesk when
Windows HDR is enabled. HdrGuard runs locally on the Windows machine being controlled.

## Requirements

- Windows 10 or Windows 11.
- RustDesk installed on the Windows machine being controlled.
- At least one HDR-capable display if you want HdrGuard to change HDR state.

Release ZIPs are self-contained `win-x64` builds. You do not need to install the .NET
runtime for the published ZIP.

## Download and run

1. Download `HdrGuard-win-x64.zip` from the GitHub release.
2. Extract it to a folder you control.
3. Run `HdrGuard.exe`.

The tray icon menu includes:

- `Enabled` - pause or resume automatic guarding.
- `Turn HDR on` / `Turn HDR off` - manual display HDR commands.
- `Restore HDR now` - restore only displays currently guarded by HdrGuard.
- `Open config` / `Open log` - open local runtime files.
- `Exit` - close the tray app.

Windows SmartScreen may warn because the first open-source releases are not code-signed.
Verify the release SHA256 checksum before running if you need stronger provenance.

## Install at login

From the extracted release folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1 -Start
```

By default, the installer keeps the app in the extracted folder and creates a Startup
shortcut. To install somewhere else:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1 -InstallDir C:\Tools\HdrGuard -Start
```

It also creates this Startup shortcut:

```text
%AppData%\Microsoft\Windows\Start Menu\Programs\Startup\HdrGuard.lnk
```

To uninstall the app but keep your config/log/state files:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\uninstall.ps1
```

To also remove the app-local `data` folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\uninstall.ps1 -RemoveUserData
```

## Manual commands

These commands run once and exit; they do not start the tray app.

```powershell
HdrGuard.exe --hdr-on
HdrGuard.exe --hdr-off
```

`--hdr-on` also clears guarded state, so a pending restore will not fight a manual
recovery action.

## Configuration

On first run, HdrGuard creates:

```text
.\data\config.json
.\data\state.json
.\data\HdrGuard.log
```

The `data` folder is next to `HdrGuard.exe`, so a portable install keeps its runtime
configuration with the software folder. If you upgrade from an older release, HdrGuard
copies missing files from `%AppData%\HdrGuard` into `.\data` once and does not overwrite
files that already exist in `.\data`.

The default config is equivalent to `config.example.json`:

```json
{
  "startEnabled": true,
  "rustDesk": {
    "pollSeconds": 2,
    "startupScanLines": 200,
    "enableNetworkFallback": false,
    "logPaths": [
      "%AppData%\\RustDesk\\log\\cm\\RustDesk_rCURRENT.log",
      "%ProgramData%\\RustDesk\\log\\cm\\RustDesk_rCURRENT.log"
    ],
    "connectPatterns": [
      "Got new connection"
    ],
    "disconnectPatterns": [
      "cm ipc connection (closed|disconnect)"
    ]
  },
  "rules": [
    {
      "process": "rustdesk.exe",
      "disableHdrWhenActive": true,
      "restoreAfterMinutes": 5,
      "maxDisabledMinutes": 60
    }
  ]
}
```

Detection is log-first. HdrGuard watches RustDesk connection-manager logs for connect
and disconnect lines. Additional `connectPatterns` and `disconnectPatterns` extend the
built-in regexes; they do not replace them.

`enableNetworkFallback` is off by default because RustDesk can keep background network
connections open that are not active remote-control sessions.

## Troubleshooting

- If the tray stays idle during a RustDesk session, open the log and verify the RustDesk
  connection-manager log path in `config.json`.
- If HDR is restored for 0 displays, display identifiers may have changed after a
  driver update, monitor reconnect, or topology change. Use `HdrGuard.exe --hdr-on` as
  the recovery command.
- If HDR returns too soon or too late, adjust `restoreAfterMinutes`.
- If HdrGuard misses a disconnect, `maxDisabledMinutes` is the safety cap that restores
  HDR even without a disconnect log event.

## Build from source

Install the .NET 10 SDK, then run:

```powershell
dotnet test .\tests\HdrGuard.Tests\HdrGuard.Tests.csproj
dotnet publish .\src\HdrGuard\HdrGuard.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

To build a release ZIP:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\package-release.ps1
```

## Privacy

HdrGuard reads local RustDesk logs only to infer session connect/disconnect state. It
does not send telemetry, upload logs, or contact a service. See `PRIVACY.md` before
sharing logs in issues.

## Contributing

Read `CONTRIBUTING.md` before sending changes. This public repository has strict
rules for avoiding personal configs, logs, machine paths, and private working notes.

## License

Apache-2.0. See `LICENSE` and `NOTICE`.
