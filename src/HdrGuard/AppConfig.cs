using System.Text.Json;
using System.Text.Json.Serialization;

namespace HdrGuard;

internal sealed class AppConfig
{
    public bool startEnabled { get; set; } = true;

    public RustDeskMonitorConfig rustDesk { get; set; } = new();

    public List<AppRule> rules { get; set; } =
    [
        new()
    ];

    [JsonIgnore]
    public AppRule PrimaryRule => rules.FirstOrDefault() ?? new AppRule();

    public static AppConfig LoadOrCreate(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (!File.Exists(path))
        {
            var config = new AppConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions.Indented));
            return config;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions.Default) ?? new AppConfig();
        }
        catch (Exception ex)
        {
            var backupPath = $"{path}.invalid-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(path, backupPath, overwrite: true);
            var config = new AppConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions.Indented));
            AppLog.Write($"Config load failed. Backed up invalid config to {backupPath}. {ex}");
            return config;
        }
    }
}

internal sealed class AppRule
{
    public string process { get; set; } = "rustdesk.exe";

    public bool disableHdrWhenActive { get; set; } = true;

    // Idle delay after a RustDesk disconnect before HDR is restored.
    // Default biases toward restoring the normal Windows environment soon while
    // still tolerating brief reconnects. Choose 3, 5, 20, or any custom value.
    public int restoreAfterMinutes { get; set; } = 5;

    // Hard cap on how long HdrGuard keeps HDR disabled. Acts as a fail-safe when a
    // disconnect event is missed (e.g. macOS lid-close skips graceful close logs).
    // 0 disables the fail-safe.
    public int maxDisabledMinutes { get; set; } = 60;
}

internal sealed class RustDeskMonitorConfig
{
    // Built-in detection baselines, kept here as the single source of truth. The monitor
    // always applies these and merges any config patterns on top (union), so a config that
    // predates a log-string fix still gets the corrected baseline. RustDesk's connection
    // manager (src/ui_cm_interface.rs) emits three teardown lines:
    // "cm ipc connection closed: {err}", "... closed from connection request", and
    // "... disconnect" (the graceful client-initiated path). The single disconnect regex
    // below covers all three; verified stable across RustDesk 1.2.3 -> master.
    internal static readonly string[] BuiltInConnectPatterns =
    [
        "Got new connection"
    ];

    internal static readonly string[] BuiltInDisconnectPatterns =
    [
        "cm ipc connection (closed|disconnect)"
    ];

    public int pollSeconds { get; set; } = 2;

    public int startupScanLines { get; set; } = 200;

    public bool enableNetworkFallback { get; set; }

    public string[] logPaths { get; set; } =
    [
        "%AppData%\\RustDesk\\log\\cm\\RustDesk_rCURRENT.log",
        "%ProgramData%\\RustDesk\\log\\cm\\RustDesk_rCURRENT.log"
    ];

    public string[] connectPatterns { get; set; } = [.. BuiltInConnectPatterns];

    public string[] disconnectPatterns { get; set; } = [.. BuiltInDisconnectPatterns];
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static readonly JsonSerializerOptions Indented = new(Default)
    {
        WriteIndented = true
    };
}
