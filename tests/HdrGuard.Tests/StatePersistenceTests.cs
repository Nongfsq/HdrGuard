using HdrGuard;

namespace HdrGuard.Tests;

/// <summary>
/// Verifies persistence and config-default behavior that the reliability pass depends on:
/// the new <c>restoreDueAtUtc</c>/<c>failSafeLatched</c> fields survive a round trip, new
/// configs default to 5/60, and an existing valid config is never rewritten.
/// </summary>
[TestClass]
public class StatePersistenceTests
{
    private string _dir = "";

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(Path.GetTempPath(), "HdrGuardTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [TestMethod]
    public void GuardState_RoundTrips_RestoreDeadlineAndLatch()
    {
        var path = Path.Combine(_dir, "state.json");
        var due = new DateTimeOffset(2026, 5, 30, 12, 5, 0, TimeSpan.Zero);
        var original = new GuardState
        {
            disabledByGuard = true,
            disabledAtUtc = due.AddMinutes(-5),
            restoreDueAtUtc = due,
            failSafeLatched = true,
            displays = [new ManagedDisplayState { targetId = 1, name = "Display 1" }]
        };

        original.Save(path);
        var loaded = GuardState.Load(path);

        Assert.IsTrue(loaded.disabledByGuard);
        Assert.AreEqual(due, loaded.restoreDueAtUtc);
        Assert.IsTrue(loaded.failSafeLatched);
        Assert.AreEqual(1, loaded.displays.Count);
        Assert.AreEqual("Display 1", loaded.displays[0].name);
    }

    [TestMethod]
    public void GuardState_Clear_ResetsNewFields()
    {
        var state = new GuardState
        {
            disabledByGuard = true,
            disabledAtUtc = DateTimeOffset.UtcNow,
            restoreDueAtUtc = DateTimeOffset.UtcNow.AddMinutes(5),
            failSafeLatched = true,
            displays = [new ManagedDisplayState { targetId = 0, name = "Display 0" }]
        };

        state.Clear();

        Assert.IsFalse(state.disabledByGuard);
        Assert.IsNull(state.disabledAtUtc);
        Assert.IsNull(state.restoreDueAtUtc);
        Assert.IsFalse(state.failSafeLatched);
        Assert.AreEqual(0, state.displays.Count);
    }

    [TestMethod]
    public void GuardState_LoadsLegacyFile_WithoutNewFields()
    {
        // A pre-reliability-pass state file has no restoreDueAtUtc or failSafeLatched.
        var path = Path.Combine(_dir, "legacy-state.json");
        File.WriteAllText(path, """
            {
              "disabledByGuard": true,
              "disabledAtUtc": "2026-05-30T12:00:00+00:00",
              "displays": [{ "adapterLowPart": 1, "adapterHighPart": 0, "targetId": 2, "name": "Legacy" }]
            }
            """);

        var loaded = GuardState.Load(path);

        Assert.IsTrue(loaded.disabledByGuard);
        Assert.IsNull(loaded.restoreDueAtUtc);
        Assert.IsFalse(loaded.failSafeLatched);
        Assert.AreEqual(1, loaded.displays.Count);
        Assert.AreEqual("Legacy", loaded.displays[0].name);
    }

    [TestMethod]
    public void AppConfig_NewConfig_UsesReliabilityDefaults()
    {
        var path = Path.Combine(_dir, "config.json");

        var config = AppConfig.LoadOrCreate(path);

        Assert.AreEqual(5, config.PrimaryRule.restoreAfterMinutes);
        Assert.AreEqual(60, config.PrimaryRule.maxDisabledMinutes);
        Assert.IsFalse(config.rustDesk.enableNetworkFallback, "network fallback must stay off by default");
        Assert.IsTrue(File.Exists(path), "new config should be written to disk");
    }

    [TestMethod]
    public void AppConfig_ExistingConfig_IsNotOverwritten()
    {
        // A user who previously chose 20 minutes must keep it after the upgrade.
        var path = Path.Combine(_dir, "config.json");
        var userJson = """
            {
              "startEnabled": true,
              "rules": [
                { "process": "rustdesk.exe", "disableHdrWhenActive": true, "restoreAfterMinutes": 20 }
              ]
            }
            """;
        File.WriteAllText(path, userJson);

        var config = AppConfig.LoadOrCreate(path);

        Assert.AreEqual(20, config.PrimaryRule.restoreAfterMinutes, "existing restore delay must be preserved");
        // maxDisabledMinutes was absent in the old file, so it takes the safe default.
        Assert.AreEqual(60, config.PrimaryRule.maxDisabledMinutes);
        Assert.AreEqual(userJson, File.ReadAllText(path), "valid existing config must not be rewritten");
    }
}
