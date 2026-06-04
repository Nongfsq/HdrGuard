namespace HdrGuard;

internal static class AppPaths
{
    public static string AppDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HdrGuard");

    public static string ConfigPath { get; } = Path.Combine(AppDataDirectory, "config.json");

    public static string StatePath { get; } = Path.Combine(AppDataDirectory, "state.json");

    public static string LogPath { get; } = Path.Combine(AppDataDirectory, "HdrGuard.log");
}
