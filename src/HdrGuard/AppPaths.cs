namespace HdrGuard;

internal static class AppPaths
{
    private static readonly string[] RuntimeFileNames = ["config.json", "state.json", "HdrGuard.log"];

    public static string ApplicationDirectory { get; } = AppContext.BaseDirectory;

    public static string RuntimeDirectory { get; } = Path.Combine(ApplicationDirectory, "data");

    public static string LegacyAppDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HdrGuard");

    public static string ConfigPath { get; } = Path.Combine(RuntimeDirectory, "config.json");

    public static string StatePath { get; } = Path.Combine(RuntimeDirectory, "state.json");

    public static string LogPath { get; } = Path.Combine(RuntimeDirectory, "HdrGuard.log");

    public static IReadOnlyList<string> MigrateLegacyRuntimeFiles()
    {
        return MigrateRuntimeFiles(LegacyAppDataDirectory, RuntimeDirectory);
    }

    internal static IReadOnlyList<string> MigrateRuntimeFiles(string sourceDirectory, string destinationDirectory)
    {
        var migrated = new List<string>();

        if (!Directory.Exists(sourceDirectory))
        {
            return migrated;
        }

        Directory.CreateDirectory(destinationDirectory);

        foreach (var fileName in RuntimeFileNames)
        {
            var sourcePath = Path.Combine(sourceDirectory, fileName);
            var destinationPath = Path.Combine(destinationDirectory, fileName);

            if (!File.Exists(sourcePath) || File.Exists(destinationPath))
            {
                continue;
            }

            try
            {
                File.Copy(sourcePath, destinationPath, overwrite: false);
                migrated.Add(fileName);
            }
            catch (Exception ex)
            {
                AppLog.Write($"Could not migrate legacy runtime file {sourcePath} to {destinationPath}. {ex}");
            }
        }

        return migrated;
    }
}
