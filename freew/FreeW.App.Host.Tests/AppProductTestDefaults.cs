using System.Runtime.CompilerServices;
using Free.Shared.AppServices;

// Block every test in this assembly until the WPF keeper-thread warm-up has completed.
// WpfWarmUpGateAttribute extends Xunit.Sdk.BeforeAfterTestAttribute and calls
// WpfTestWarmUp.EnsureReady() before each test method executes.  The keeper thread itself
// is started fire-and-forget by WpfTestWarmUp.StartWarmUp()'s [ModuleInitializer] so there
// is no blocking on the CLR type-init guard.
[assembly: FreeW.App.Host.Tests.WpfWarmUpGate]

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
