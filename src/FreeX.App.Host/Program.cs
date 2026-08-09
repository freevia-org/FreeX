namespace FreeX.App.Host;

/// <summary>
/// Explicit application entry point.
///
/// <para>
/// Velopack must run before WPF initializes: when the app is launched by the installer/updater
/// with hook arguments (install / update / uninstall), <see cref="VelopackBootstrap.Run"/> services
/// the hook and exits the process immediately — before any WPF <see cref="System.Windows.Application"/>
/// is constructed. On a normal launch it returns instantly and startup proceeds.
/// </para>
///
/// <para>
/// To make this the real entry point, App.xaml is compiled as a Page (not an ApplicationDefinition)
/// so the WPF SDK does not generate its own <c>Main</c>; see FreeX.App.Host.csproj.
/// </para>
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        // Velopack first — before the WPF Application exists. Exits fast on hook invocations.
        // The .Run() call is kept here at the entry point so Velopack's entry-point detection
        // recognizes it; hook configuration lives in VelopackBootstrap.Configure().
        VelopackBootstrap.Configure().Run();

        // Install FreeX's product identity before any App code runs, so the shared storage
        // helpers resolve %LOCALAPPDATA%\FreeX (settings, recent files, autosave, diagnostics)
        // and never fall back to the neutral default. Must precede the first storage-path read,
        // which happens in App startup's AppOptionsStore.Load().
        Free.Shared.AppServices.AppProduct.Current =
            FreeX.App.Services.FreeXApplicationStartupDescriptor.ProductIdentity;

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
