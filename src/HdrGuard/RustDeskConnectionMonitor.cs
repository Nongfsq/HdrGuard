using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace HdrGuard;

internal sealed class RustDeskConnectionMonitor
{
    private readonly AppConfig _config;
    private readonly Regex[] _connectPatterns;
    private readonly Regex[] _disconnectPatterns;
    private readonly Dictionary<string, long> _positions = new(StringComparer.OrdinalIgnoreCase);
    private bool _connectedFromLogs;
    private bool _connectObservedThisPoll;

    public RustDeskConnectionMonitor(AppConfig config)
    {
        _config = config;
        // Always apply the built-in baselines and merge any config patterns on top. A config
        // that predates a log-string fix (e.g. one missing the graceful-disconnect pattern)
        // still gets the corrected detection; config patterns can only extend the baseline.
        _connectPatterns = MergePatterns(RustDeskMonitorConfig.BuiltInConnectPatterns, config.rustDesk.connectPatterns);
        _disconnectPatterns = MergePatterns(RustDeskMonitorConfig.BuiltInDisconnectPatterns, config.rustDesk.disconnectPatterns);
    }

    private static Regex[] MergePatterns(IEnumerable<string> builtIn, IEnumerable<string> configured) =>
        builtIn
            .Concat(configured ?? [])
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => pattern.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(ToRegex)
            .ToArray();

    public void Prime()
    {
        foreach (var path in ResolveLogPaths())
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var lines = ReadLastLines(path, Math.Max(0, _config.rustDesk.startupScanLines));
                foreach (var line in lines)
                {
                    ApplyLine(line);
                }

                _positions[path] = new FileInfo(path).Length;
            }
            catch (Exception ex)
            {
                AppLog.Write($"Failed to prime RustDesk log {path}. {ex.Message}");
            }
        }
    }

    public RustDeskConnectionSnapshot Poll()
    {
        // Reset per-poll observation so connect lines seen during Prime never leak
        // into the first real poll.
        _connectObservedThisPoll = false;

        var events = 0;
        foreach (var path in ResolveLogPaths())
        {
            events += ReadNewEvents(path);
        }

        var networkActive = false;
        if (_config.rustDesk.enableNetworkFallback)
        {
            networkActive = RustDeskNetworkProbe.HasEstablishedConnection(_config.PrimaryRule.process);
        }

        var connected = _connectedFromLogs || networkActive;
        var reason = events > 0
            ? $"log events: {events}"
            : networkActive
                ? "network fallback active"
                : _connectedFromLogs
                    ? "last RustDesk log state is connected"
                    : "last RustDesk log state is idle";

        return new RustDeskConnectionSnapshot(connected, _connectObservedThisPoll, reason);
    }

    private int ReadNewEvents(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return 0;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!_positions.TryGetValue(path, out var position) || position > stream.Length)
            {
                position = 0;
            }

            stream.Seek(position, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            var events = 0;
            while (reader.ReadLine() is { } line)
            {
                if (ApplyLine(line))
                {
                    events++;
                }
            }

            _positions[path] = stream.Position;
            return events;
        }
        catch (Exception ex)
        {
            AppLog.Write($"Failed to read RustDesk log {path}. {ex.Message}");
            return 0;
        }
    }

    private bool ApplyLine(string line)
    {
        if (_connectPatterns.Any(pattern => pattern.IsMatch(line)))
        {
            _connectedFromLogs = true;
            _connectObservedThisPoll = true;
            return true;
        }

        if (_disconnectPatterns.Any(pattern => pattern.IsMatch(line)))
        {
            _connectedFromLogs = false;
            return true;
        }

        return false;
    }

    private IEnumerable<string> ResolveLogPaths()
    {
        foreach (var configuredPath in _config.rustDesk.logPaths)
        {
            var expanded = Environment.ExpandEnvironmentVariables(configuredPath);
            if (expanded.IndexOfAny(['*', '?']) < 0)
            {
                yield return expanded;
                continue;
            }

            var directory = Path.GetDirectoryName(expanded);
            var pattern = Path.GetFileName(expanded);
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(pattern) || !Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(directory, pattern).OrderByDescending(File.GetLastWriteTimeUtc).Take(3))
            {
                yield return path;
            }
        }
    }

    private static Regex ToRegex(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static IEnumerable<string> ReadLastLines(string path, int maxLines)
    {
        if (maxLines == 0 || !File.Exists(path))
        {
            return [];
        }

        var queue = new Queue<string>(maxLines);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (queue.Count == maxLines)
            {
                queue.Dequeue();
            }

            queue.Enqueue(line);
        }

        return queue;
    }
}

internal sealed record RustDeskConnectionSnapshot(bool IsConnected, bool ConnectObserved, string Reason);

internal static class RustDeskNetworkProbe
{
    private const int AF_INET = 2;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const int NO_ERROR = 0;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    private const int MIB_TCP_STATE_ESTAB = 5;

    public static bool HasEstablishedConnection(string processName)
    {
        try
        {
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(processName);
            var pids = Process.GetProcessesByName(nameWithoutExtension)
                .Select(process => process.Id)
                .ToHashSet();

            if (pids.Count == 0)
            {
                return false;
            }

            return GetTcpRows().Any(row =>
                row.state == MIB_TCP_STATE_ESTAB &&
                pids.Contains((int)row.owningPid) &&
                IsRemoteAddress(row.remoteAddr));
        }
        catch (Exception ex)
        {
            AppLog.Write($"Network fallback failed. {ex.Message}");
            return false;
        }
    }

    private static IEnumerable<MIB_TCPROW_OWNER_PID> GetTcpRows()
    {
        var bufferSize = 0;
        var result = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        if (result != ERROR_INSUFFICIENT_BUFFER)
        {
            yield break;
        }

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            result = GetExtendedTcpTable(buffer, ref bufferSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
            if (result != NO_ERROR)
            {
                yield break;
            }

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPtr = IntPtr.Add(buffer, sizeof(uint));
            var rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
            for (var i = 0; i < rowCount; i++)
            {
                yield return Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                rowPtr = IntPtr.Add(rowPtr, rowSize);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool IsRemoteAddress(uint remoteAddress)
    {
        if (remoteAddress == 0)
        {
            return false;
        }

        var bytes = BitConverter.GetBytes(remoteAddress);
        var address = new IPAddress(bytes);
        return !IPAddress.IsLoopback(address);
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int dwOutBufLen,
        bool sort,
        int ipVersion,
        int tblClass,
        uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint state;
        public uint localAddr;
        public uint localPort;
        public uint remoteAddr;
        public uint remotePort;
        public uint owningPid;
    }
}
