using HdrGuard;

namespace HdrGuard.Tests;

/// <summary>
/// Drives the real <see cref="RustDeskConnectionMonitor"/> against a temp log file to
/// verify the connect/disconnect/connect-observed signal path end to end, with no
/// RustDesk process and no network probe. This is the automated stand-in for HDR-007's
/// "one simulated connect/disconnect cycle".
/// </summary>
[TestClass]
public class RustDeskConnectionMonitorTests
{
    private string _dir = "";
    private string _logPath = "";

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(Path.GetTempPath(), "HdrGuardMonTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _logPath = Path.Combine(_dir, "RustDesk_rCURRENT.log");
        File.WriteAllText(_logPath, "");
        AppLog.Initialize(Path.Combine(_dir, "HdrGuard.log"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private RustDeskConnectionMonitor NewMonitor()
    {
        var config = new AppConfig();
        config.rustDesk.enableNetworkFallback = false; // log-only, no TCP probe
        config.rustDesk.logPaths = [_logPath];
        var monitor = new RustDeskConnectionMonitor(config);
        monitor.Prime();
        return monitor;
    }

    private RustDeskConnectionMonitor NewMonitor(string[] connectPatterns, string[] disconnectPatterns)
    {
        var config = new AppConfig();
        config.rustDesk.enableNetworkFallback = false;
        config.rustDesk.logPaths = [_logPath];
        config.rustDesk.connectPatterns = connectPatterns;
        config.rustDesk.disconnectPatterns = disconnectPatterns;
        var monitor = new RustDeskConnectionMonitor(config);
        monitor.Prime();
        return monitor;
    }

    private void Append(string line) => File.AppendAllText(_logPath, line + Environment.NewLine);

    [TestMethod]
    public void SimulatedCycle_ConnectThenDisconnect_IsDetected()
    {
        var monitor = NewMonitor();

        // Idle to start.
        Assert.IsFalse(monitor.Poll().IsConnected);

        // Connect line appears.
        Append("2026-05-30 12:00:00 INFO Got new connection from 10.0.0.5");
        var connected = monitor.Poll();
        Assert.IsTrue(connected.IsConnected);
        Assert.IsTrue(connected.ConnectObserved, "fresh connect line should be observed this poll");

        // A subsequent poll with no new lines stays connected but observes no new connect.
        var stillConnected = monitor.Poll();
        Assert.IsTrue(stillConnected.IsConnected);
        Assert.IsFalse(stillConnected.ConnectObserved);

        // Disconnect line appears.
        Append("2026-05-30 12:01:00 INFO cm ipc connection closed");
        var disconnected = monitor.Poll();
        Assert.IsFalse(disconnected.IsConnected);
        Assert.IsFalse(disconnected.ConnectObserved);
    }

    [TestMethod]
    public void Prime_RecoversConnectedState_WithoutReportingConnectObserved()
    {
        // Log already shows a live session before HdrGuard starts.
        Append("2026-05-30 11:59:00 INFO Got new connection from 10.0.0.5");

        var monitor = NewMonitor(); // Prime() scans existing lines.
        var first = monitor.Poll();

        Assert.IsTrue(first.IsConnected, "primed state should reflect the existing connection");
        Assert.IsFalse(first.ConnectObserved, "connect seen during Prime must not count as a fresh connect");
    }

    [TestMethod]
    public void Reconnect_AfterDisconnect_ReportsConnectObservedAgain()
    {
        var monitor = NewMonitor();

        Append("Got new connection");
        monitor.Poll();
        Append("cm ipc connection closed from connection request");
        Assert.IsFalse(monitor.Poll().IsConnected);

        Append("Got new connection");
        var reconnect = monitor.Poll();
        Assert.IsTrue(reconnect.IsConnected);
        Assert.IsTrue(reconnect.ConnectObserved);
    }

    [TestMethod]
    public void LegacyConfig_MissingGracefulPattern_StillDetectsDisconnect()
    {
        // A frozen pre-fix config carrying only the two old patterns. The built-in baseline
        // must still catch RustDesk's graceful "cm ipc connection disconnect" teardown line.
        var monitor = NewMonitor(
            connectPatterns: ["Got new connection"],
            disconnectPatterns: ["cm ipc connection closed", "connection closed from connection request"]);

        Append("2026-05-30 12:00:00 INFO Got new connection from 10.0.0.5");
        Assert.IsTrue(monitor.Poll().IsConnected);

        // This exact line is NOT matched by either legacy pattern; only the baseline regex
        // "cm ipc connection (closed|disconnect)" covers it.
        Append("2026-05-30 12:05:00 INFO cm ipc connection disconnect");
        Assert.IsFalse(monitor.Poll().IsConnected, "graceful disconnect must be detected via the built-in baseline");
    }

    [TestMethod]
    public void EmptyConfigPatterns_FallBackToBuiltInBaseline()
    {
        // A config that cleared its pattern arrays still detects connect/disconnect from the
        // built-in baseline; config patterns can only extend it, never remove it.
        var monitor = NewMonitor(connectPatterns: [], disconnectPatterns: []);

        Append("Got new connection");
        Assert.IsTrue(monitor.Poll().IsConnected);

        Append("cm ipc connection closed: stream error");
        Assert.IsFalse(monitor.Poll().IsConnected);
    }

    [TestMethod]
    public void MissingLogPath_PollsIdleWithoutThrowing()
    {
        var config = new AppConfig();
        config.rustDesk.enableNetworkFallback = false;
        config.rustDesk.logPaths = [Path.Combine(_dir, "missing", "RustDesk_rCURRENT.log")];
        config.rustDesk.startupScanLines = 50;
        var monitor = new RustDeskConnectionMonitor(config);

        monitor.Prime();
        var snapshot = monitor.Poll();

        Assert.IsFalse(snapshot.IsConnected);
        Assert.IsFalse(snapshot.ConnectObserved);
    }

    [TestMethod]
    public void CustomConfigPattern_WorksAlongsideBaseline()
    {
        // A user-supplied custom pattern is merged with the baseline: both the custom line
        // and the built-in graceful-disconnect line are detected.
        var monitor = NewMonitor(
            connectPatterns: ["custom session opened"],
            disconnectPatterns: ["custom session ended"]);

        Append("custom session opened");
        Assert.IsTrue(monitor.Poll().IsConnected, "custom connect pattern should work");

        Append("custom session ended");
        Assert.IsFalse(monitor.Poll().IsConnected, "custom disconnect pattern should work");

        // Baseline still active alongside the custom patterns.
        Append("Got new connection");
        Assert.IsTrue(monitor.Poll().IsConnected);
        Append("cm ipc connection disconnect");
        Assert.IsFalse(monitor.Poll().IsConnected, "baseline disconnect still applies with custom patterns present");
    }
}
