using System.Windows;
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

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

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
}
