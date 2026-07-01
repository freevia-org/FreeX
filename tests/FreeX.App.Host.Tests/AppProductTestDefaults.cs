using System.Runtime.CompilerServices;
using Free.Shared.AppServices;
using Free.Shared.Shell;

namespace FreeX.App.Host.Tests;

// Installs FreeX's product identity for this test assembly (mirrors Program.Main) so the
// shared storage/diagnostics helpers resolve the "FreeX"/"FREEX_DIAGNOSTICS" footprint that
// the path tests assert. Runs once on assembly load, before any test.
internal static class AppProductTestDefaults
{
    [ModuleInitializer]
    public static void Initialize()
    {
        AppProduct.Current = new AppProductIdentity("FreeX", "FREEX_DIAGNOSTICS", "FreeX");
        // The neutral backstage planners (greeting, recent-file list) now live in the shared
        // shell and resolve strings via BackstageStrings.Current; install FreeX's catalog so
        // their assertions match UiText, exactly as App.xaml.cs does at runtime.
        BackstageStrings.Current = new ResourceBackstageStrings(UiText.Get, UiText.Format);
    }
}
