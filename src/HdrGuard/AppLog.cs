namespace HdrGuard;

internal static class AppLog
{
    private static string? _path;

    public static string Path => _path ?? throw new InvalidOperationException("AppLog is not initialized.");

    public static void Initialize(string path)
    {
        _path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
    }

    public static void Write(string message)
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            File.AppendAllText(_path, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never break tray or HDR control flows.
        }
    }
}
