namespace HdrGuard;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        AppLog.Initialize(AppPaths.LogPath);

        try
        {
            if (TryRunCommand(args))
            {
                return;
            }

            using var mutex = new Mutex(initiallyOwned: true, "Local\\HdrGuard", out var createdNew);
            if (!createdNew)
            {
                MessageBox.Show("HdrGuard is already running.", "HdrGuard", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var config = AppConfig.LoadOrCreate(AppPaths.ConfigPath);
            var state = GuardState.Load(AppPaths.StatePath);
            Application.Run(new HdrGuardApplicationContext(config, state));
        }
        catch (Exception ex)
        {
            AppLog.Write($"Fatal startup error. {ex}");
            MessageBox.Show(ex.Message, "HdrGuard startup failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static bool TryRunCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return false;
        }

        var command = args[0].Trim().ToLowerInvariant();
        if (command is not ("--hdr-on" or "--hdr-off"))
        {
            return false;
        }

        var enabled = command == "--hdr-on";
        var changed = new HdrController().SetHdrForAllSupportedDisplays(enabled);
        AppLog.Write($"Command {command} changed {changed} display(s).");

        if (enabled)
        {
            var state = GuardState.Load(AppPaths.StatePath);
            state.Clear();
            state.Save(AppPaths.StatePath);
        }

        return true;
    }
}
