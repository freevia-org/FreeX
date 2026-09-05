using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// shared-window-lifecycle F1: AppOptions.QuickAccessToolbarCommands / QuickAccessToolbarBelowRibbon
/// are held on a single process-wide AppOptions instance every MainWindow shares (see
/// FreeXOptionsRuntimeSession -- registered as one DI singleton in App.xaml.cs), so customizing the
/// Quick Access Toolbar in one window must rebuild every OTHER open window's QAT chrome immediately
/// too -- exactly like the sibling Show Formula Bar broadcast
/// (<see cref="WorkbookWindowRegistryFormulaBarTests"/>, MainWindow.ViewCommands.cs) -- instead of
/// leaving sibling windows visually stale until they happen to rebuild their own QAT for an
/// unrelated reason.
/// </summary>
public sealed class QuickAccessToolbarWindowBroadcastTests
{
    private static MainWindow CreateWindow(
        WorkbookWindowRegistry? registry,
        FreeXOptionsRuntimeSession optionsRuntimeSession)
    {
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1");
        var workbookRef = new WorkbookRef { Current = workbook };
        var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
        return new MainWindow(
            NullLogger<MainWindow>.Instance,
            new ViewportService(),
            commandBus,
            new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
            [],
            workbookRef,
            workbook,
            NullUserMessageService.Instance,
            windowRegistry: registry,
            optionsRuntimeSession: optionsRuntimeSession)
        {
            WindowState = WindowState.Normal,
            Width = 1280,
            Height = 720
        };
    }

    // r446: delegates to the one fixed implementation -- see DispatcherTestPump.
    private static void PumpDispatcher() => DispatcherTestPump.PumpDispatcher();

    // Stands in for App.xaml.cs's DI-singleton FreeXOptionsRuntimeSession: an in-memory
    // load/save pair so the test never touches the real, disk-backed AppOptionsStore, while still
    // reproducing the exact shared-instance mutation FreeXOptionsRuntimeSession.Adopt performs
    // (LiveOptions.CopyFrom(...) in place) that every window's `_options` field aliases.
    private static FreeXOptionsRuntimeSession CreateInMemoryOptionsSession()
    {
        var sharedOptions = new AppOptions
        {
            QuickAccessToolbarCommands = QuickAccessToolbarCatalog.DefaultCommandIds.ToList()
        };
        return new FreeXOptionsRuntimeSession(
            initialOptions: sharedOptions,
            load: () => sharedOptions.Clone(),
            save: _ => true);
    }

    [Fact]
    public void ApplyQuickAccessToolbarCustomization_BroadcastsToOtherRegisteredWindows() =>
        StaTestRunner.Run(() =>
        {
            var registry = new WorkbookWindowRegistry();
            var window = CreateWindow(registry, CreateInMemoryOptionsSession());
            var sibling = new TestWorkbookWindow();
            try
            {
                window.Show();
                window.Activate();
                PumpDispatcher(); // MainWindow_Loaded -> RegisterWithWindowRegistry() registers `window`.

                // A second open window in the same process (View > New Window, or a second
                // File > New) -- a lightweight fake stands in for it exactly as in
                // R114_CommandBusWorkbookSwapRetireTests, since only the broadcast wiring is
                // under test, not a second full WPF window.
                registry.Register(sibling);

                window.ApplyQuickAccessToolbarCustomizationForTest(
                    QuickAccessToolbarCommandIds.Print,
                    QuickAccessToolbarCustomizationAction.Add);

                sibling.QuickAccessToolbarChangedAppliedCount.Should().Be(1,
                    "the Quick Access Toolbar is an Excel-instance-wide preference (shared AppOptions), " +
                    "so every other open window must rebuild its own QAT chrome immediately, exactly like " +
                    "the sibling Show Formula Bar broadcast -- not only the next time it happens to " +
                    "rebuild its QAT for an unrelated reason");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });

    [Fact]
    public void ApplyQuickAccessToolbarCustomization_NoRegistry_StillUpdatesThisWindowsOwnToolbar() =>
        StaTestRunner.Run(() =>
        {
            // Adjacent case: the ordinary single-window session (no WorkbookWindowRegistry at
            // all, e.g. ReusableFreeXMainWindowSession) must be unaffected by the new broadcast --
            // `_windowRegistry?.BroadcastQuickAccessToolbarChanged(this)` is a no-op, and this
            // window's own customization + rebuild (the pre-existing, already-correct behavior)
            // must still apply.
            var window = CreateWindow(registry: null, CreateInMemoryOptionsSession());
            try
            {
                window.Show();
                window.Activate();
                PumpDispatcher();

                window.ApplyQuickAccessToolbarCustomizationForTest(
                    QuickAccessToolbarCommandIds.Print,
                    QuickAccessToolbarCustomizationAction.Add);

                window.OptionsForTest.QuickAccessToolbarCommands.Should().Contain(QuickAccessToolbarCommandIds.Print);
                window.GetQuickAccessToolbarButtonForTest(QuickAccessToolbarCommandIds.Print).Should().NotBeNull(
                    "this window's own QAT must still rebuild with the newly added command");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });

    /// <summary>
    /// C4 (round 165 remediation): the still-open half of this same defect the round 164 fixer
    /// disclosed but did not close -- MainWindow.Backstage.cs's ShowOptionsDialog commits the
    /// Options dialog's Quick Access Toolbar page (same shared AppOptions instance,
    /// QuickAccessBelowRibbonCheckBox / the QAT add/remove/move controls) and rebuilt only ITS OWN
    /// window's QAT chrome (RebuildQuickAccessToolbar()) with no broadcast, unlike the QAT context
    /// menu's ApplyQuickAccessToolbarCustomization above.
    /// </summary>
    [Fact]
    public void ShowOptionsDialog_OkCommit_BroadcastsToOtherRegisteredWindows() =>
        StaTestRunner.Run(() =>
        {
            var registry = new WorkbookWindowRegistry();
            var window = CreateWindow(registry, CreateInMemoryOptionsSession());
            var sibling = new TestWorkbookWindow();
            try
            {
                window.Show();
                window.Activate();
                PumpDispatcher(); // MainWindow_Loaded -> RegisterWithWindowRegistry() registers `window`.

                registry.Register(sibling);

                InvokeShowOptionsDialogAndClickOk(window);

                sibling.QuickAccessToolbarChangedAppliedCount.Should().Be(1,
                    "the Options dialog's Quick Access Toolbar page edits the exact same " +
                    "process-wide AppOptions instance as the QAT context menu, so committing it " +
                    "with OK must broadcast to every other open window exactly like " +
                    "ApplyQuickAccessToolbarCustomization already does, instead of leaving sibling " +
                    "windows' QAT chrome stale");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });

    /// <summary>
    /// Adjacent case: the ordinary single-window session (no WorkbookWindowRegistry at all) must be
    /// unaffected by the new broadcast -- the null-conditional
    /// `_windowRegistry?.BroadcastQuickAccessToolbarChanged(this)` is a no-op, and the dialog's own
    /// commit (the pre-existing, already-correct behavior) must still succeed without throwing.
    /// </summary>
    [Fact]
    public void ShowOptionsDialog_NoRegistry_StillCommitsWithoutError() =>
        StaTestRunner.Run(() =>
        {
            var window = CreateWindow(registry: null, CreateInMemoryOptionsSession());
            try
            {
                window.Show();
                window.Activate();
                PumpDispatcher();

                InvokeShowOptionsDialogAndClickOk(window);
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });

    /// <summary>
    /// Drives the real "File &gt; Options" entry point end to end: invokes SsOptionsBtn_Click
    /// (MainWindow.Backstage.cs), which synchronously opens the genuinely modal OptionsDialog via
    /// ShowDialog(). While that call blocks and pumps the dispatcher, a queued callback locates the
    /// dialog through the owner window's OwnedWindows (no test-only seam) and raises its own OkBtn's
    /// Click event -- the same control the user's mouse click drives -- so the broadcast wiring
    /// under test is whatever MainWindow.Backstage.cs actually calls, not a callback the test
    /// supplies itself.
    /// </summary>
    private static void InvokeShowOptionsDialogAndClickOk(MainWindow window)
    {
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            var dialog = window.OwnedWindows
                .OfType<Window>()
                .FirstOrDefault(w => w.GetType().Name == "OptionsDialog");
            if (dialog is null)
                return;

            var okBtnField = dialog.GetType().GetField(
                    "OkBtn", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new MissingMemberException("OptionsDialog", "OkBtn");
            var okBtn = (Button)okBtnField.GetValue(dialog)!;
            okBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, okBtn));
        }), DispatcherPriority.ApplicationIdle);

        window.SsOptionsBtn_Click(window, new RoutedEventArgs());
        PumpDispatcher();
    }
}
