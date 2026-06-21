namespace FreeW.App.Host;

/// <summary>
/// FreeW entry point. Installs FreeW identity/seams, then delegates the common WPF options,
/// diagnostics, and application-run lifecycle to the shared startup runner.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main()
        => WpfApplicationStartupRunner.Run(new WpfApplicationStartupSpec<FreeWOptions>(
            new AppProductIdentity("FreeW", "FREEW_DIAGNOSTICS", "FreeW"),
            (options, optionsStore) => new MainWindow(options, optionsStore))
        {
            InstallSharedSeams = () => ShellStrings.Current = new DefaultShellStrings()
        });
}
