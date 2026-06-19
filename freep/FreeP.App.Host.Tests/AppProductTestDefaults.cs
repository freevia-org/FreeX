using System.Runtime.CompilerServices;
using Free.Shared.AppServices;

namespace FreeP.App.Host.Tests;

// Installs FreeP's product identity for this test assembly (mirrors Program.Main) so the shared
// storage/diagnostics helpers resolve the "FreeP"/"FREEP_DIAGNOSTICS" footprint. Runs once on assembly
// load, before any test.
internal static class AppProductTestDefaults
{
    [ModuleInitializer]
    public static void Initialize() =>
        AppProduct.Current = new AppProductIdentity("FreeP", "FREEP_DIAGNOSTICS", "FreeP");
}
