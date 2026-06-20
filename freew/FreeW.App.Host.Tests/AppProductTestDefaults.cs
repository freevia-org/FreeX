using System.Runtime.CompilerServices;
using Free.Shared.AppServices;

namespace FreeW.App.Host.Tests;

// Installs FreeW's product identity for this test assembly (mirrors Program.Main) so the shared
// storage/diagnostics helpers resolve the "FreeW"/"FREEW_DIAGNOSTICS" footprint. Runs once on
// assembly load, before any test.
internal static class AppProductTestDefaults
{
    [ModuleInitializer]
    public static void Initialize() =>
        AppProduct.Current = new AppProductIdentity("FreeW", "FREEW_DIAGNOSTICS", "FreeW");
}
