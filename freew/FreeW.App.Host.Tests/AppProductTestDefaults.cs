using System.Runtime.CompilerServices;
using System.IO;
using Free.Shared.AppServices;
using FreeW.App.Host;
using FreeW.App.Presentation.Shell;

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
    public static void Initialize()
    {
        AppProduct.Current = new AppProductIdentity("FreeW", "FREEW_DIAGNOSTICS", "FreeW");
        IsolateAutosaveRecovery();
    }

    // A window opened by a test offers every pending snapshot of the per-user recovery folder in a modal
    // dialog on load. Autosave files left there by an earlier killed test host (or the developer's own
    // FreeW) would make any such test hang on that dialog, so every window a test opens without its own
    // session factory gets a private, initially empty recovery folder, removed when the run ends.
    private static void IsolateAutosaveRecovery()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FreeW.Host.Tests", "recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        AutosaveCoordinator.DefaultSessionFactory = ports =>
            new FreeWAutosaveSession(ports, new AutosaveSnapshotStore(directory));
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        };
    }
}
