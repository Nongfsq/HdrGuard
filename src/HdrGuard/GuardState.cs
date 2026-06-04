using System.Text.Json;

namespace HdrGuard;

internal sealed class GuardState
{
    public bool disabledByGuard { get; set; }

    public DateTimeOffset? disabledAtUtc { get; set; }

    // Persisted restore deadline (UTC). Survives restarts so HdrGuard resumes the
    // existing delay instead of starting a fresh one. Null when no restore is pending.
    public DateTimeOffset? restoreDueAtUtc { get; set; }

    // Set when the missed-disconnect fail-safe restored HDR while RustDesk still
    // looked connected. Prevents immediately re-disabling until a new session begins.
    public bool failSafeLatched { get; set; }

    public List<ManagedDisplayState> displays { get; set; } = [];

    public static GuardState Load(string path)
    {
        if (!File.Exists(path))
        {
            return new GuardState();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<GuardState>(json, JsonOptions.Default) ?? new GuardState();
        }
        catch (Exception ex)
        {
            AppLog.Write($"State load failed. Ignoring state file. {ex}");
            return new GuardState();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions.Indented));
    }

    public void Clear()
    {
        disabledByGuard = false;
        disabledAtUtc = null;
        restoreDueAtUtc = null;
        failSafeLatched = false;
        displays.Clear();
    }
}

internal sealed class ManagedDisplayState
{
    public uint adapterLowPart { get; set; }

    public int adapterHighPart { get; set; }

    public uint targetId { get; set; }

    public string name { get; set; } = "";

    public DisplayKey ToDisplayKey() => new(adapterLowPart, adapterHighPart, targetId);

    public static ManagedDisplayState From(DisplayState display) => new()
    {
        adapterLowPart = display.Key.AdapterLowPart,
        adapterHighPart = display.Key.AdapterHighPart,
        targetId = display.Key.TargetId,
        name = display.Name
    };
}
