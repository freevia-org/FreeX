using System;
using System.Windows;

namespace FreeW.App.Host;

/// <summary>
/// FreeW entry point. Installs FreeW's own product identity and shell strings into the shared
/// tier (so storage/diagnostics land in %LOCALAPPDATA%\FreeW, not FreeX), then shows the window.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        // Same contract FreeX uses — set identity before any shared storage path is resolved.
        AppProduct.Current = new AppProductIdentity("FreeW", "FREEW_DIAGNOSTICS", "FreeW");
        ShellStrings.Current = new DefaultShellStrings();

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Run(new MainWindow());
    }
}
