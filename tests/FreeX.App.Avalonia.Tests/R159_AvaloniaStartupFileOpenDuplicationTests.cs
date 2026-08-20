namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R159-shared-startup-recovery-F1: <c>App.OnFrameworkInitializationCompleted</c> used to run TWO
/// independent, unsynchronized passes over the startup-argument file paths -- a synchronous one
/// (<c>StartupWorkbookLoader.ResolveAdditionalOpenableFilePaths</c> +
/// <c>OpenAdditionalStartupFileWindows</c>) immediately after constructing the primary
/// <see cref="MainWindow"/>, and a second one inside <c>CompleteStartupAsync</c>
/// (<c>StartupFileOpenPlanner.Plan(startupArguments, ...)</c>) posted to the dispatcher a moment
/// later. Both passes derived their file list from the SAME unfiltered
/// <c>App.StartupArguments</c>, so every startup path after the first was opened once by each pass
/// -- two separate, unsynchronized <see cref="MainWindow"/> instances editing the same file, where
/// whichever saves last silently clobbers the other. The fix removes the synchronous pass and
/// leaves <c>StartupFileOpenPlanner.Plan</c> inside <c>CompleteStartupAsync</c> as the SOLE opener,
/// mirroring the WPF host's single-pass <c>App.xaml.cs</c> (<c>StartupFileOpenPlanner.Plan</c> at
/// its own single call site).
///
/// <para>
/// Driving the real <see cref="Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime"/>
/// startup path end-to-end (real windows, real dispatcher pump) is impractical from a unit test,
/// so -- matching this project's established convention for pinning <c>App.cs</c> shell-wiring
/// contracts that can't be driven live (see <c>AvaloniaAutosaveOwnershipSourceTests</c>,
/// <c>AvaloniaSaveExecutionOwnershipSourceTests</c>, etc.) -- this reads the shipping source and
/// pins the single-mechanism contract directly.
/// </para>
/// </summary>
public sealed class R159_AvaloniaStartupFileOpenDuplicationTests
{
    private static string AppSource => TestWorkspaceFileLocator.ReadAllText(
        "src",
        "FreeX.App.Avalonia",
        "App.cs");

    [Fact]
    public void OnFrameworkInitializationCompleted_DoesNotAlsoOpenAdditionalStartupFilesSynchronously()
    {
        var source = AppSource;

        // The synchronous R133 pass (and the method it drove) must be gone: it duplicated every
        // startup path after the first, which CompleteStartupAsync's StartupFileOpenPlanner.Plan
        // (below) already opens on its own, single, dispatcher-deferred pass.
        source.Should().NotContain(
            "OpenAdditionalStartupFileWindows",
            "the synchronous additional-file-window pass duplicated every startup path after the " +
            "first against CompleteStartupAsync's StartupFileOpenPlanner.Plan pass and must be removed, " +
            "not merely reordered");
        source.Should().NotContain(
            "ResolveAdditionalOpenableFilePaths",
            "resolving 'additional' openable paths synchronously in OnFrameworkInitializationCompleted " +
            "recreated the exact path list StartupFileOpenPlanner.Plan derives moments later from the " +
            "same unfiltered StartupArguments, which is what caused the duplicate opens");

        // The single surviving mechanism -- CompleteStartupAsync's plan-and-open pass -- must still
        // be present and still reasoning over the full, original StartupArguments list (not a
        // pre-filtered subset), exactly like the WPF host's App.xaml.cs single call site.
        source.Should().Contain("StartupFileOpenPlanner.Plan(startupArguments, recoveryAccepted)");
        source.Should().Contain(
            "Dispatcher.UIThread.Post(() => _ = CompleteStartupAsync(mainWindow, snapshotStore, StartupArguments));");
    }

    [Fact]
    public void CompleteStartupAsync_StillOpensEveryPlanEntryExactlyOnce_IntoEitherThePrimaryOrANewWindow()
    {
        // Sibling / no-regression: the single surviving mechanism must still route every planned
        // entry somewhere -- the primary window when the plan says so, a brand-new independent
        // window otherwise -- with no leftover branch that skips or double-opens an entry. This is
        // the exact loop shape that must survive the removal of the synchronous duplicate pass.
        var source = AppSource;

        // Individual (line-ending-agnostic) fragments of CompleteStartupAsync's plan-consuming
        // loop -- the sole remaining place that turns a StartupFileOpenPlan entry into an opened
        // window.
        source.Should().Contain("var targetWindow = entry.OpenInNewWindow");
        source.Should().Contain("? OpenIndependentWindow()");
        source.Should().Contain(": mainWindow;");
        source.Should().Contain("await targetWindow.OpenStartupFileAsync(entry.Path);");
    }
}
