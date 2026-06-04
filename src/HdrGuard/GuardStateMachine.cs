namespace HdrGuard;

/// <summary>
/// The HDR action the host should perform after evaluating the current situation.
/// </summary>
internal enum GuardAction
{
    /// <summary>Take no HDR action.</summary>
    None,

    /// <summary>Disable HDR for the active RustDesk session.</summary>
    DisableHdr,

    /// <summary>Restore HDR because the configured idle delay elapsed.</summary>
    RestoreHdr,

    /// <summary>Restore HDR because the missed-disconnect fail-safe tripped.</summary>
    FailSafeRestoreHdr
}

/// <summary>
/// All external facts the guard needs to make one decision. Pure values so the
/// decision logic can be unit tested without a tray, a timer, or real displays.
/// </summary>
internal readonly record struct GuardInputs(
    DateTimeOffset NowUtc,
    bool GuardEnabled,
    bool DisableHdrWhenActive,
    bool RustDeskConnected,
    bool ConnectObserved,
    int RestoreAfterMinutes,
    int MaxDisabledMinutes);

/// <summary>
/// The result of one evaluation: the action to take plus the pending restore
/// deadline and fail-safe latch the caller should persist.
/// </summary>
internal readonly record struct GuardDecision(
    GuardAction Action,
    DateTimeOffset? RestoreDueAtUtc,
    bool FailSafeLatched,
    string Reason);

/// <summary>
/// Pure decision logic for HdrGuard. Given the current persisted <see cref="GuardState"/>
/// and the latest <see cref="GuardInputs"/>, it returns what to do without performing any
/// side effect. The application context executes the decision against real displays and
/// persists state; tests exercise this method directly.
/// </summary>
internal static class GuardStateMachine
{
    public static GuardDecision Evaluate(GuardState state, GuardInputs inputs)
    {
        var due = state.restoreDueAtUtc;
        var latched = state.failSafeLatched;

        // While paused or with the rule disabled, HdrGuard makes no changes. Pending
        // state is preserved so resuming continues from where it left off.
        if (!inputs.GuardEnabled || !inputs.DisableHdrWhenActive)
        {
            return new GuardDecision(GuardAction.None, due, latched, "guard paused or rule disabled");
        }

        // A fresh connect line means a genuinely new session, so any fail-safe latch
        // left over from a stale "connected" log no longer applies.
        if (inputs.ConnectObserved)
        {
            latched = false;
        }

        if (inputs.RustDeskConnected)
        {
            // An active session cancels any pending restore.
            due = null;

            if (state.disabledByGuard)
            {
                if (FailSafeElapsed(state, inputs))
                {
                    // Disconnect detection likely failed: restore HDR even though the
                    // log still reads connected, then latch to avoid immediate re-disable.
                    return new GuardDecision(GuardAction.FailSafeRestoreHdr, null, true,
                        "max disabled minutes exceeded while still connected");
                }

                return new GuardDecision(GuardAction.None, null, latched,
                    "rustdesk connected, hdr already disabled by guard");
            }

            if (latched)
            {
                // We already fail-safe-restored this stale session; wait for a real
                // disconnect or a new connect before touching HDR again.
                return new GuardDecision(GuardAction.None, null, true,
                    "fail-safe latched; awaiting new session or disconnect");
            }

            return new GuardDecision(GuardAction.DisableHdr, null, false, "rustdesk connected");
        }

        // RustDesk is idle. A real disconnect ends the session, so drop any latch.
        latched = false;

        if (!state.disabledByGuard)
        {
            return new GuardDecision(GuardAction.None, null, false, "rustdesk idle, nothing guarded");
        }

        // The fail-safe still bounds the disabled window even after a normal disconnect.
        if (FailSafeElapsed(state, inputs))
        {
            return new GuardDecision(GuardAction.FailSafeRestoreHdr, null, false,
                "max disabled minutes exceeded");
        }

        // Reuse the persisted deadline if one exists so a restart does not restart the
        // delay; otherwise schedule one now.
        due = state.restoreDueAtUtc ?? inputs.NowUtc.AddMinutes(Math.Max(1, inputs.RestoreAfterMinutes));

        if (inputs.NowUtc >= due)
        {
            return new GuardDecision(GuardAction.RestoreHdr, null, false, "restore delay elapsed");
        }

        return new GuardDecision(GuardAction.None, due, false, "restore scheduled");
    }

    private static bool FailSafeElapsed(GuardState state, GuardInputs inputs)
    {
        if (inputs.MaxDisabledMinutes <= 0)
        {
            return false;
        }

        if (state.disabledAtUtc is not { } disabledAt)
        {
            return false;
        }

        return inputs.NowUtc - disabledAt >= TimeSpan.FromMinutes(inputs.MaxDisabledMinutes);
    }
}
