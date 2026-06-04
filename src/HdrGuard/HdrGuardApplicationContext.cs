using System.Diagnostics;

namespace HdrGuard;

internal sealed class HdrGuardApplicationContext : ApplicationContext
{
    private readonly AppConfig _config;
    private readonly GuardState _state;
    private readonly RustDeskConnectionMonitor _monitor;
    private readonly HdrController _hdrController = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _enabledMenuItem;
    private readonly ToolStripMenuItem _statusMenuItem;
    private readonly ToolStripMenuItem _restoreMenuItem;
    private readonly System.Windows.Forms.Timer _timer;

    private bool _enabled;
    private bool _rustDeskActive;
    private bool _tickInProgress;
    private string _lastSignal = "Starting";
    private string _lastAction = "none yet";

    public HdrGuardApplicationContext(AppConfig config, GuardState state)
    {
        _config = config;
        _state = state;
        _enabled = config.startEnabled;
        _monitor = new RustDeskConnectionMonitor(config);

        _enabledMenuItem = new ToolStripMenuItem("Enabled")
        {
            Checked = _enabled,
            CheckOnClick = true
        };
        _enabledMenuItem.CheckedChanged += (_, _) =>
        {
            _enabled = _enabledMenuItem.Checked;
            AppLog.Write($"Guard enabled changed to {_enabled}.");
            UpdateTrayText();
        };

        _statusMenuItem = new ToolStripMenuItem("Status: starting")
        {
            Enabled = false
        };

        _restoreMenuItem = new ToolStripMenuItem("Restore HDR now", null, (_, _) => RestoreHdrNow());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_enabledMenuItem);
        menu.Items.Add(_statusMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Turn HDR on", null, (_, _) => SetHdrForAllDisplays(true)));
        menu.Items.Add(new ToolStripMenuItem("Turn HDR off", null, (_, _) => SetHdrForAllDisplays(false)));
        menu.Items.Add(_restoreMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open config", null, (_, _) => OpenPath(AppPaths.ConfigPath)));
        menu.Items.Add(new ToolStripMenuItem("Open log", null, (_, _) => OpenPath(AppPaths.LogPath)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication()));

        _notifyIcon = new NotifyIcon
        {
            Icon = AppIcon.Load(),
            ContextMenuStrip = menu,
            Text = "HdrGuard",
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowStatusBalloon();

        _timer = new System.Windows.Forms.Timer
        {
            Interval = Math.Max(1, config.rustDesk.pollSeconds) * 1000
        };
        _timer.Tick += (_, _) => Tick();

        _monitor.Prime();

        Tick();
        _timer.Start();
    }

    private void Tick()
    {
        if (_tickInProgress)
        {
            return;
        }

        _tickInProgress = true;
        try
        {
            var snapshot = _monitor.Poll();
            _lastSignal = snapshot.Reason;

            if (snapshot.IsConnected != _rustDeskActive)
            {
                _rustDeskActive = snapshot.IsConnected;
                AppLog.Write($"RustDesk active changed to {_rustDeskActive}: {snapshot.Reason}");
            }

            var rule = _config.PrimaryRule;
            var inputs = new GuardInputs(
                NowUtc: DateTimeOffset.UtcNow,
                GuardEnabled: _enabled,
                DisableHdrWhenActive: rule.disableHdrWhenActive,
                RustDeskConnected: snapshot.IsConnected,
                ConnectObserved: snapshot.ConnectObserved,
                RestoreAfterMinutes: rule.restoreAfterMinutes,
                MaxDisabledMinutes: rule.maxDisabledMinutes);

            var decision = GuardStateMachine.Evaluate(_state, inputs);
            ApplyDecision(decision);

            UpdateTrayText();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Tick failed. {ex}");
            _lastSignal = ex.Message;
            UpdateTrayText();
        }
        finally
        {
            _tickInProgress = false;
        }
    }

    private void ApplyDecision(GuardDecision decision)
    {
        switch (decision.Action)
        {
            case GuardAction.DisableHdr:
                DisableHdrForRustDesk(decision);
                break;

            case GuardAction.RestoreHdr:
                RestoreHdrNow(failSafe: false, decision.Reason);
                break;

            case GuardAction.FailSafeRestoreHdr:
                RestoreHdrNow(failSafe: true, decision.Reason);
                break;

            case GuardAction.None:
            default:
                PersistPendingState(decision);
                break;
        }
    }

    // Keep the persisted restore deadline and fail-safe latch in sync when no HDR
    // change is needed. Writes only when something actually changed.
    private void PersistPendingState(GuardDecision decision)
    {
        if (!_state.disabledByGuard)
        {
            return;
        }

        if (_state.restoreDueAtUtc == decision.RestoreDueAtUtc && _state.failSafeLatched == decision.FailSafeLatched)
        {
            return;
        }

        var hadDeadline = _state.restoreDueAtUtc is not null;
        _state.restoreDueAtUtc = decision.RestoreDueAtUtc;
        _state.failSafeLatched = decision.FailSafeLatched;
        _state.Save(AppPaths.StatePath);

        if (!hadDeadline && decision.RestoreDueAtUtc is { } due)
        {
            AppLog.Write($"Scheduled HDR restore at {due:O}: {decision.Reason}");
        }
    }

    private void DisableHdrForRustDesk(GuardDecision decision)
    {
        if (_state.disabledByGuard)
        {
            return;
        }

        var enabledDisplays = _hdrController.GetDisplays()
            .Where(display => display.AdvancedColorSupported && display.AdvancedColorEnabled)
            .ToList();

        if (enabledDisplays.Count == 0)
        {
            return;
        }

        foreach (var display in enabledDisplays)
        {
            _hdrController.SetHdr(display.Key, enabled: false);
        }

        _state.disabledByGuard = true;
        _state.disabledAtUtc = DateTimeOffset.UtcNow;
        _state.restoreDueAtUtc = null;
        _state.failSafeLatched = decision.FailSafeLatched;
        _state.displays = enabledDisplays.Select(ManagedDisplayState.From).ToList();
        _state.Save(AppPaths.StatePath);
        _lastAction = $"disabled HDR on {enabledDisplays.Count} display(s)";
        AppLog.Write($"Disabled HDR on {enabledDisplays.Count} display(s) for RustDesk.");
        ShowBalloon("HDR disabled", "RustDesk connection detected. HdrGuard turned HDR off.");
    }

    private void RestoreHdrNow() => RestoreHdrNow(failSafe: false, "manual restore");

    private void RestoreHdrNow(bool failSafe, string reason)
    {
        if (!_state.disabledByGuard)
        {
            return;
        }

        var restored = 0;
        var currentDisplays = _hdrController.GetDisplays().ToDictionary(display => display.Key);
        foreach (var display in _state.displays)
        {
            try
            {
                if (currentDisplays.TryGetValue(display.ToDisplayKey(), out var current) && current.AdvancedColorSupported)
                {
                    _hdrController.SetHdr(current.Key, enabled: true);
                    restored++;
                }
            }
            catch (Exception ex)
            {
                AppLog.Write($"Failed to restore HDR for {display.name}. {ex}");
            }
        }

        if (restored == 0)
        {
            // The recorded displays could not be matched (ids changed after a driver update,
            // monitor reconnect, or topology change). Fall back to all currently supported
            // displays so HDR still comes back. Log distinctly so this is visible without
            // guessing from the restore count.
            var supported = currentDisplays.Values.Where(display => display.AdvancedColorSupported).ToList();
            if (_state.displays.Count > 0)
            {
                AppLog.Write(
                    $"Recorded display ids no longer match; restoring HDR on all {supported.Count} supported display(s).");
            }

            foreach (var display in supported)
            {
                try
                {
                    _hdrController.SetHdr(display.Key, enabled: true);
                    restored++;
                }
                catch (Exception ex)
                {
                    AppLog.Write($"Fallback HDR restore failed for {display.Name}. {ex}");
                }
            }
        }

        // The fail-safe path may fire while RustDesk still looks connected, so it must
        // latch to avoid immediately re-disabling. A normal restore clears all state.
        var latch = failSafe && _rustDeskActive;
        _state.Clear();
        _state.failSafeLatched = latch;
        _state.Save(AppPaths.StatePath);

        var kind = failSafe ? "Fail-safe restored" : "Restored";
        _lastAction = $"{(failSafe ? "fail-safe restored" : "restored")} HDR on {restored} display(s)";
        AppLog.Write($"{kind} HDR on {restored} display(s). Reason: {reason}.");
        ShowBalloon(
            failSafe ? "HDR restored (fail-safe)" : "HDR restored",
            restored == 0
                ? "No guarded displays needed restoring."
                : $"{(failSafe ? "Fail-safe restore: " : "")}Restored HDR on {restored} display(s).",
            failSafe ? ToolTipIcon.Warning : ToolTipIcon.Info);
        UpdateTrayText();
    }

    private void SetHdrForAllDisplays(bool enabled)
    {
        try
        {
            var changed = _hdrController.SetHdrForAllSupportedDisplays(enabled);
            if (enabled)
            {
                _state.Clear();
                _state.Save(AppPaths.StatePath);
            }

            _lastAction = $"manual HDR {(enabled ? "on" : "off")} on {changed} display(s)";
            AppLog.Write($"Manual HDR {(enabled ? "on" : "off")} changed {changed} display(s).");
            ShowBalloon(enabled ? "HDR on" : "HDR off", $"Changed {changed} display(s).");
            UpdateTrayText();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Manual HDR set failed. {ex}");
            ShowBalloon("HDR change failed", ex.Message, ToolTipIcon.Error);
        }
    }

    private void UpdateTrayText()
    {
        var status = _enabled ? "enabled" : "paused";
        var rustDesk = _rustDeskActive ? "RustDesk connected" : "RustDesk idle";
        var due = _state.restoreDueAtUtc;
        var restore = due is null ? "" : $", restore {FormatRemaining(due.Value - DateTimeOffset.UtcNow)}";
        var guarded = _state.disabledByGuard ? ", HDR off" : "";
        var text = $"HdrGuard: {status}, {rustDesk}{restore}";

        _statusMenuItem.Text = $"Status: {status}, {rustDesk}{guarded}{restore}";
        _restoreMenuItem.Enabled = _state.disabledByGuard;
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    private void ShowStatusBalloon()
    {
        ShowBalloon("HdrGuard", $"{_statusMenuItem.Text}\nSignal: {_lastSignal}\nLast action: {_lastAction}");
    }

    private void ShowBalloon(string title, string body, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = body;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(3000);
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return "now";
        }

        if (remaining.TotalMinutes >= 1)
        {
            return $"in {Math.Ceiling(remaining.TotalMinutes)}m";
        }

        return $"in {Math.Ceiling(remaining.TotalSeconds)}s";
    }

    private static void OpenPath(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "");
            }

            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Write($"Failed to open {path}. {ex}");
        }
    }

    private void ExitApplication()
    {
        _timer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
