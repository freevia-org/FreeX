namespace FreeP.App.Host;

/// <summary>
/// FreeP entry point. Keeps FreeP-specific identity/seam/window choices local while the shared WPF
/// runner owns options loading, local diagnostics, crash hooks, and app lifetime events.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        // TODO(velopack): if/when a shared Velopack bootstrap helper lands, call it here before the WPF
        // Application is created. The scaffold ships without self-update.

        WpfApplicationStartupRunner.Run(new WpfApplicationStartupSpec<FreePOptions>(
            new AppProductIdentity("FreeP", "FREEP_DIAGNOSTICS", "FreeP"),
            (options, optionsStore) => new MainWindow(options, optionsStore))
        {
            InstallSharedSeams = AppComposition.InstallSharedSeams
        });
    }
}
