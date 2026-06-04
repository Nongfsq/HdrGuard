# Privacy

HdrGuard is local-only software.

## What it reads

- RustDesk connection-manager logs from paths configured in `config.json`.
- Windows display configuration through DisplayConfig APIs.
- Local TCP table information only if `enableNetworkFallback` is explicitly enabled.

## What it writes

HdrGuard writes local runtime files under:

```text
.\data
```

Those files are:

- `config.json` - user-editable settings.
- `state.json` - displays HdrGuard changed and pending restore state.
- `HdrGuard.log` - local diagnostics and HDR actions.

The `data` folder is created next to `HdrGuard.exe`. Older releases used
`%AppData%\HdrGuard`; current releases copy missing files from that legacy directory
into `.\data` once and do not overwrite existing app-local files.

## What it does not do

- No telemetry.
- No network calls to this project.
- No cloud sync.
- No collection of RustDesk credentials.

## Before sharing logs

RustDesk logs and HdrGuard logs may contain local paths, device names, timestamps, or
network details. Remove personal or sensitive information before posting logs in a
public GitHub issue.
