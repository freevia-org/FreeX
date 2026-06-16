using System.Runtime.CompilerServices;
using Free.Shared.AppServices;

namespace FreeX.App.Host.Tests;

// Installs FreeX's product identity for this test assembly (mirrors Program.Main) so the
// shared storage/diagnostics helpers resolve the "FreeX"/"FREEX_DIAGNOSTICS" footprint that
// the path tests assert. Runs once on assembly load, before any test.
internal static class AppProductTestDefaults
{
    [ModuleInitializer]
    public static void Initialize() =>
        AppProduct.Current = new AppProductIdentity("FreeX", "FREEX_DIAGNOSTICS", "FreeX");
}
