using HdrGuard;

namespace HdrGuard.Tests;

/// <summary>
/// Exercises the pure guard decision logic. These tests never touch real displays;
/// they assert the <see cref="GuardDecision"/> returned for a given state plus inputs,
/// and use <see cref="Apply"/> to simulate how the host mutates persisted state so
/// multi-tick sequences (restart, reconnect, fail-safe) can be verified end to end.
/// </summary>
[TestClass]
public class GuardStateMachineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    private static GuardInputs Inputs(
        DateTimeOffset now,
        bool connected,
        bool connectObserved = false,
        bool enabled = true,
        bool disableWhenActive = true,
        int restoreAfterMinutes = 5,
        int maxDisabledMinutes = 60) =>
        new(now, enabled, disableWhenActive, connected, connectObserved, restoreAfterMinutes, maxDisabledMinutes);

    /// <summary>
    /// Mirrors how <c>HdrGuardApplicationContext</c> applies a decision to persisted
    /// state, without performing any real HDR change. Returns the simulated number of
    /// guarded displays so disable/restore can be asserted.
    /// </summary>
    private static void Apply(GuardState state, GuardDecision decision, GuardInputs inputs, int displaysToGuard = 2)
    {
        switch (decision.Action)
        {
            case GuardAction.DisableHdr:
                state.disabledByGuard = true;
                state.disabledAtUtc = inputs.NowUtc;
                state.restoreDueAtUtc = null;
                state.failSafeLatched = decision.FailSafeLatched;
                state.displays = Enumerable.Range(0, displaysToGuard)
                    .Select(i => new ManagedDisplayState { targetId = (uint)i, name = $"Display {i}" })
                    .ToList();
                break;

            case GuardAction.RestoreHdr:
                state.Clear();
                break;

            case GuardAction.FailSafeRestoreHdr:
                state.Clear();
                state.failSafeLatched = inputs.RustDeskConnected;
                break;

            case GuardAction.None:
            default:
                if (state.disabledByGuard)
                {
                    state.restoreDueAtUtc = decision.RestoreDueAtUtc;
                    state.failSafeLatched = decision.FailSafeLatched;
                }
                break;
        }
    }

    [TestMethod]
    public void Connect_WhenNothingGuarded_DisablesHdr()
    {
        var state = new GuardState();

        var decision = GuardStateMachine.Evaluate(state, Inputs(T0, connected: true, connectObserved: true));

        Assert.AreEqual(GuardAction.DisableHdr, decision.Action);
        Assert.IsNull(decision.RestoreDueAtUtc);
        Assert.IsFalse(decision.FailSafeLatched);
    }

    [TestMethod]
    public void Connect_WhenAlreadyGuarded_DoesNothing()
    {
        var state = new GuardState();
        Apply(state, GuardStateMachine.Evaluate(state, Inputs(T0, connected: true, connectObserved: true)), Inputs(T0, true));

        var decision = GuardStateMachine.Evaluate(state, Inputs(T0.AddSeconds(2), connected: true));

        Assert.AreEqual(GuardAction.None, decision.Action);
        Assert.IsTrue(state.disabledByGuard);
    }

    [TestMethod]
    public void Disconnect_AfterGuard_SchedulesRestoreThenRestoresWhenDue()
    {
        var state = new GuardState();
        var connect = Inputs(T0, connected: true, connectObserved: true);
        Apply(state, GuardStateMachine.Evaluate(state, connect), connect);

        // First idle tick schedules the restore deadline but takes no action yet.
        var disconnect = Inputs(T0.AddSeconds(2), connected: false);
        var scheduled = GuardStateMachine.Evaluate(state, disconnect);
        Apply(state, scheduled, disconnect);

        Assert.AreEqual(GuardAction.None, scheduled.Action);
        Assert.AreEqual(T0.AddSeconds(2).AddMinutes(5), scheduled.RestoreDueAtUtc);
        Assert.AreEqual(scheduled.RestoreDueAtUtc, state.restoreDueAtUtc);

        // Before the deadline: still waiting.
        var beforeDue = GuardStateMachine.Evaluate(state, Inputs(T0.AddMinutes(4), connected: false));
        Assert.AreEqual(GuardAction.None, beforeDue.Action);

        // At/after the deadline: restore.
        var afterDue = GuardStateMachine.Evaluate(state, Inputs(T0.AddMinutes(6), connected: false));
        Assert.AreEqual(GuardAction.RestoreHdr, afterDue.Action);
        Apply(state, afterDue, Inputs(T0.AddMinutes(6), connected: false));
        Assert.IsFalse(state.disabledByGuard);
        Assert.IsNull(state.restoreDueAtUtc);
    }

    [TestMethod]
    public void Reconnect_DuringRestoreDelay_CancelsPendingRestore()
    {
        var state = new GuardState();
        var connect = Inputs(T0, connected: true, connectObserved: true);
        Apply(state, GuardStateMachine.Evaluate(state, connect), connect);

        var disconnect = Inputs(T0.AddSeconds(2), connected: false);
        Apply(state, GuardStateMachine.Evaluate(state, disconnect), disconnect);
        Assert.IsNotNull(state.restoreDueAtUtc);

        // A new connection mid-delay cancels the pending restore and keeps HDR off.
        var reconnect = Inputs(T0.AddMinutes(2), connected: true, connectObserved: true);
        var decision = GuardStateMachine.Evaluate(state, reconnect);
        Apply(state, decision, reconnect);

        Assert.AreEqual(GuardAction.None, decision.Action);
        Assert.IsNull(decision.RestoreDueAtUtc);
        Assert.IsNull(state.restoreDueAtUtc);
        Assert.IsTrue(state.disabledByGuard);

        // After the reconnect, a later disconnect schedules a fresh delay from that point.
        var disconnect2 = Inputs(T0.AddMinutes(3), connected: false);
        var rescheduled = GuardStateMachine.Evaluate(state, disconnect2);
        Assert.AreEqual(GuardAction.None, rescheduled.Action);
        Assert.AreEqual(T0.AddMinutes(3).AddMinutes(5), rescheduled.RestoreDueAtUtc);
    }

    [TestMethod]
    public void Restart_WithPersistedDeadline_ReusesItInsteadOfRestartingDelay()
    {
        // Simulate a state file written before a restart: HDR disabled, restore due soon.
        var persisted = new GuardState
        {
            disabledByGuard = true,
            disabledAtUtc = T0,
            restoreDueAtUtc = T0.AddMinutes(5),
            displays = [new ManagedDisplayState { targetId = 0, name = "Display 0" }]
        };

        // HdrGuard restarts 4 minutes later. It must keep the original deadline.
        var afterRestart = GuardStateMachine.Evaluate(persisted, Inputs(T0.AddMinutes(4), connected: false));
        Assert.AreEqual(GuardAction.None, afterRestart.Action);
        Assert.AreEqual(T0.AddMinutes(5), afterRestart.RestoreDueAtUtc);

        // Once the original deadline passes, it restores — without waiting a fresh 5 min.
        var atDeadline = GuardStateMachine.Evaluate(persisted, Inputs(T0.AddMinutes(5).AddSeconds(1), connected: false));
        Assert.AreEqual(GuardAction.RestoreHdr, atDeadline.Action);
    }

    [TestMethod]
    public void MissedDisconnect_WhileStillConnected_TripsFailSafeAndLatches()
    {
        var state = new GuardState();
        var connect = Inputs(T0, connected: true, connectObserved: true);
        Apply(state, GuardStateMachine.Evaluate(state, connect), connect);

        // The disconnect log is never seen; RustDesk still reads connected past the cap.
        var pastCap = Inputs(T0.AddMinutes(61), connected: true, maxDisabledMinutes: 60);
        var decision = GuardStateMachine.Evaluate(state, pastCap);

        Assert.AreEqual(GuardAction.FailSafeRestoreHdr, decision.Action);
        Assert.IsTrue(decision.FailSafeLatched);

        Apply(state, decision, pastCap);
        Assert.IsFalse(state.disabledByGuard);
        Assert.IsTrue(state.failSafeLatched);

        // Latched: HDR is not re-disabled while the same stale session still reads connected.
        var stillConnected = GuardStateMachine.Evaluate(state, Inputs(T0.AddMinutes(62), connected: true));
        Assert.AreEqual(GuardAction.None, stillConnected.Action);
        Assert.IsTrue(stillConnected.FailSafeLatched);
    }

    [TestMethod]
    public void FailSafeLatch_ClearsOnNewConnectLine_AndReDisables()
    {
        var state = new GuardState { failSafeLatched = true };

        // A genuinely new connect line is observed, so the latch must release.
        var newConnect = Inputs(T0, connected: true, connectObserved: true);
        var decision = GuardStateMachine.Evaluate(state, newConnect);

        Assert.AreEqual(GuardAction.DisableHdr, decision.Action);
        Assert.IsFalse(decision.FailSafeLatched);
    }

    [TestMethod]
    public void FailSafeLatch_ClearsOnRealDisconnect()
    {
        var state = new GuardState { disabledByGuard = false, failSafeLatched = true };

        var decision = GuardStateMachine.Evaluate(state, Inputs(T0, connected: false));

        Assert.AreEqual(GuardAction.None, decision.Action);
        Assert.IsFalse(decision.FailSafeLatched);
    }

    [TestMethod]
    public void MissedDisconnect_AfterIdle_TripsFailSafeBeforeDelay()
    {
        // HDR disabled long ago; monitor now reads idle but the persisted deadline is
        // still in the future. The fail-safe cap must win over the normal delay.
        var state = new GuardState
        {
            disabledByGuard = true,
            disabledAtUtc = T0,
            restoreDueAtUtc = T0.AddMinutes(125),
            displays = [new ManagedDisplayState { targetId = 0, name = "Display 0" }]
        };

        var decision = GuardStateMachine.Evaluate(state, Inputs(T0.AddMinutes(61), connected: false, maxDisabledMinutes: 60));

        Assert.AreEqual(GuardAction.FailSafeRestoreHdr, decision.Action);
        Assert.IsFalse(decision.FailSafeLatched);
    }

    [TestMethod]
    public void MaxDisabledMinutesZero_DisablesFailSafe()
    {
        var state = new GuardState
        {
            disabledByGuard = true,
            disabledAtUtc = T0,
            displays = [new ManagedDisplayState { targetId = 0, name = "Display 0" }]
        };

        // Days later, still connected, but fail-safe is disabled: no forced restore.
        var decision = GuardStateMachine.Evaluate(state, Inputs(T0.AddDays(1), connected: true, maxDisabledMinutes: 0));

        Assert.AreEqual(GuardAction.None, decision.Action);
    }

    [TestMethod]
    public void Paused_PreservesPendingStateAndTakesNoAction()
    {
        var state = new GuardState
        {
            disabledByGuard = true,
            disabledAtUtc = T0,
            restoreDueAtUtc = T0.AddMinutes(5),
            displays = [new ManagedDisplayState { targetId = 0, name = "Display 0" }]
        };

        // Even past the deadline, a paused guard must not restore.
        var decision = GuardStateMachine.Evaluate(state, Inputs(T0.AddMinutes(10), connected: false, enabled: false));

        Assert.AreEqual(GuardAction.None, decision.Action);
        Assert.AreEqual(T0.AddMinutes(5), decision.RestoreDueAtUtc);
    }

    [TestMethod]
    public void RuleDisabled_TakesNoActionEvenWhenConnected()
    {
        var state = new GuardState();

        var decision = GuardStateMachine.Evaluate(
            state,
            Inputs(T0, connected: true, connectObserved: true, disableWhenActive: false));

        Assert.AreEqual(GuardAction.None, decision.Action);
    }

    [TestMethod]
    public void RestoreAfterMinutes_BelowOne_IsClampedToOneMinute()
    {
        var state = new GuardState
        {
            disabledByGuard = true,
            disabledAtUtc = T0,
            displays = [new ManagedDisplayState { targetId = 0, name = "Display 0" }]
        };

        var decision = GuardStateMachine.Evaluate(state, Inputs(T0, connected: false, restoreAfterMinutes: 0));

        Assert.AreEqual(GuardAction.None, decision.Action);
        Assert.AreEqual(T0.AddMinutes(1), decision.RestoreDueAtUtc);
    }
}
