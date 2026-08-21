using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Free.Shared.AppServices;
using FreeP.App.Avalonia;

namespace FreeP.App.Avalonia.Tests;

/// <summary>
/// round140 (freep-windows-print-silent-failure): FreeP.App.Avalonia's CreatePlatformPrintService used
/// to wire the real WindowsPrintService with RequirePrinterDiscoveryBeforeSubmission:false and
/// RejectNonZeroHandlerExitCode:false -- opting the whole app out of both safety checks the shared
/// service exists to provide. With discovery skipped, a stale/offline printer name is handed straight
/// to the shell "printto" verb unchecked; when the PDF handler then exits non-zero, the failure is
/// silently discarded and the submission is reported as PrintSubmissionStatus.Submitted. The sibling
/// app (FreeW.App.Avalonia/MainWindow.cs) wires `new WindowsPrintService()` with all defaults, which
/// keep both checks on.
///
/// This constructs a real MainWindow the same way production startup does
/// (`printService: null`, so the private CreatePlatformPrintService actually runs -- the exact path a
/// real user reaches via `new MainWindow()` / `new MainWindow(args)`) and inspects the real
/// WindowsPrintServiceOptions instance it wired up via reflection. This is the actual production
/// WindowsPrintService the shipped app constructs, not a stub -- reflection is only used to read the
/// private fields, not to fake the collaborator under test.
/// </summary>
public sealed class WindowsPrintServiceSubmissionSafetyTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    static WindowsPrintServiceSubmissionSafetyTests()
    {
        if (AppProduct.Current is null)
            AppProduct.Current = new AppProductIdentity("FreeP", "FREEP_DIAGNOSTICS", "FreeP");
    }

    [Fact]
    public async Task ProductionPrintServiceKeepsBothWindowsSafetyChecksEnabled()
    {
        if (!OperatingSystem.IsWindows())
            return; // The Windows print backend only exists on Windows hosts.

        object? printService = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>(), loadRecentFilesStore: null);
            printService = GetPrivateField(window, "_printService");
        });
        if (!ran)
            return; // Headless drawing unavailable in this environment; tolerate per this file's convention.

        printService.Should().NotBeNull("MainWindow's constructor always assigns _printService");
        printService!.GetType().Name.Should().Be(
            "WindowsPrintService",
            "on a Windows host CreatePlatformPrintService must select the Windows backend, " +
            "which is the real production collaborator this test needs to inspect");

        var options = GetPrivateField(printService, "_options");
        options.Should().NotBeNull("WindowsPrintService always constructs a WindowsPrintServiceOptions");

        var requireDiscovery = (bool)options!.GetType()
            .GetProperty("RequirePrinterDiscoveryBeforeSubmission")!
            .GetValue(options)!;
        var rejectNonZeroExit = (bool)options.GetType()
            .GetProperty("RejectNonZeroHandlerExitCode")!
            .GetValue(options)!;

        requireDiscovery.Should().BeTrue(
            "skipping printer-existence validation lets a stale/offline printer name reach the shell " +
            "handoff unchecked -- match FreeW.App.Avalonia's `new WindowsPrintService()` default");
        rejectNonZeroExit.Should().BeTrue(
            "ignoring a non-zero PDF handler exit code reports PrintSubmissionStatus.Submitted even " +
            "when nothing actually printed -- match FreeW.App.Avalonia's `new WindowsPrintService()` " +
            "default");
    }

    // Sibling proof: the fix must not disable Windows printing altogether, and must not touch the
    // Linux/CUPS branch's own factory wiring.
    [Fact]
    public async Task ProductionPrintServiceIsStillSupportedAndStillWindowsBacked()
    {
        if (!OperatingSystem.IsWindows())
            return;

        object? printService = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>(), loadRecentFilesStore: null);
            printService = GetPrivateField(window, "_printService");
        });
        if (!ran)
            return;

        printService.Should().NotBeNull();
        var isSupported = (bool)printService!.GetType()
            .GetProperty("IsSupported")!
            .GetValue(printService)!;
        isSupported.Should().BeTrue(
            "the fix must only change the options passed to WindowsPrintService, not stop the app from " +
            "wiring up Windows printing support at all");
    }

    private static object? GetPrivateField(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(
            fieldName,
            BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull($"expected a private instance field named '{fieldName}' on {instance.GetType()}");
        return field!.GetValue(instance);
    }

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);
}
