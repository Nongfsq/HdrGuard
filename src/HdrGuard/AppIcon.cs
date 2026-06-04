using System.Reflection;

namespace HdrGuard;

internal static class AppIcon
{
    public static Icon Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("HdrGuard.Assets.HdrGuard.ico");
        if (stream is null)
        {
            AppLog.Write("Embedded HdrGuard icon was not found; falling back to system shield icon.");
            return (Icon)SystemIcons.Shield.Clone();
        }

        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}
