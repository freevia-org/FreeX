using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using FluentAssertions;
using FreeX.App.Presentation.SheetUI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// freex-subtotals-outline-F2: the WPF host had no UI path to Data &gt; Outline &gt; Settings at all --
/// no sheet-tab context-menu entry, no ribbon command, nothing anywhere under FreeX.App.Host ever
/// constructed SetWorksheetOutlineSettingsCommand (a full source-tree grep returned zero hits) --
/// even though the Avalonia shell already exposes it from the identical sheet-tab right-click menu
/// (FreeX.App.Avalonia/MainWindow.cs:4523, MainWindow.Outline.cs's ShowOutlineSettingsDialogAsync).
/// These tests drive the real right-click -&gt; context-menu -&gt; dialog -&gt; command path end to end.
/// </summary>
public sealed class R153_SheetTabOutlineSettingsMenuTests
{
    [Fact]
    public void SheetTabContextMenu_OutlineSettingsItem_AppliesChosenSettingsToRightClickedSheetOnly()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create(sheetCount: 2);
            var activeSheet = harness.Workbook.Sheets[0];
            var targetSheet = harness.Workbook.Sheets[1];
            // Inserting Sheet2 leaves it active; re-activate Sheet1 so the test genuinely proves the
            // RIGHT-CLICKED tab (Sheet2) is targeted, not just whatever happens to be active.
            harness.ActivateSheet(activeSheet.Id);
            harness.CurrentSheetId.Should().Be(activeSheet.Id,
                "the harness must have Sheet1 active so this test proves the RIGHT-CLICKED tab (Sheet2) is targeted, not always the active sheet");

            targetSheet.OutlineSummaryBelow.Should().BeNull();
            targetSheet.OutlineSummaryRight.Should().BeNull();
            targetSheet.ApplyOutlineStyles.Should().BeNull();

            harness.OpenOutlineSettingsDialogFromSheetTabContextMenu(
                targetSheet.Name,
                dialog =>
                {
                    SetCheckBox(dialog, "OutlineSettingsSummaryBelowCheckBox", false);
                    SetCheckBox(dialog, "OutlineSettingsSummaryRightCheckBox", false);
                    SetCheckBox(dialog, "OutlineSettingsAutomaticStylesCheckBox", true);
                    ClickButton(dialog, "OutlineSettingsOkButton");
                });

            harness.DialogWasFound.Should().BeTrue(
                "the queued callback must have located the modal Outline Settings dialog via OwnedWindows to drive it at all");
            targetSheet.OutlineSummaryBelow.Should().BeFalse();
            targetSheet.OutlineSummaryRight.Should().BeFalse();
            targetSheet.ApplyOutlineStyles.Should().BeTrue();

            // Sibling assertion within the same test: the OTHER (active) sheet must be untouched --
            // this is a per-sheet setting (SetWorksheetOutlineSettingsCommand takes an explicit
            // sheetId), so applying it from Sheet2's tab must never leak onto Sheet1.
            activeSheet.OutlineSummaryBelow.Should().BeNull();
            activeSheet.OutlineSummaryRight.Should().BeNull();
            activeSheet.ApplyOutlineStyles.Should().BeNull();
        });
    }

    [Fact]
    public void SheetTabContextMenu_OutlineSettingsCancel_LeavesSheetOutlineSettingsUnchanged()
    {
        // Sibling no-regression case: dismissing the dialog via Cancel must not issue the
        // (undoable) SetWorksheetOutlineSettingsCommand at all, mirroring Avalonia's
        // OutlineSettingsPlanner.HasChanges no-op guard in ShowOutlineSettingsDialogAsync.
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var sheet = harness.Workbook.Sheets[0];

            harness.OpenOutlineSettingsDialogFromSheetTabContextMenu(
                sheet.Name,
                dialog =>
                {
                    SetCheckBox(dialog, "OutlineSettingsSummaryBelowCheckBox", false);
                    SetCheckBox(dialog, "OutlineSettingsAutomaticStylesCheckBox", true);
                    ClickButton(dialog, "OutlineSettingsCancelButton");
                });

            harness.DialogWasFound.Should().BeTrue(
                "the dialog must genuinely have been opened and canceled, not just skipped, for this no-op assertion to mean anything");
            sheet.OutlineSummaryBelow.Should().BeNull();
            sheet.OutlineSummaryRight.Should().BeNull();
            sheet.ApplyOutlineStyles.Should().BeNull();
        });
    }

    private static void SetCheckBox(Window dialog, string automationId, bool value) =>
        FindByAutomationId<CheckBox>(dialog, automationId).IsChecked = value;

    private static void ClickButton(Window dialog, string automationId) =>
        FindByAutomationId<Button>(dialog, automationId).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static T FindByAutomationId<T>(DependencyObject root, string automationId) where T : FrameworkElement =>
        WpfTestTree.FindVisualDescendants<T>(root)
            .Concat(WpfTestTree.FindLogicalDescendants<T>(root))
            .Distinct()
            .FirstOrDefault(element => string.Equals(
                AutomationProperties.GetAutomationId(element),
                automationId,
                StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"No {typeof(T).Name} with AutomationId '{automationId}' found under {root}.");

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;

        /// <summary>
        /// Set by the queued dialog-interaction callback once it actually locates the modal Outline
        /// Settings window via OwnedWindows -- asserted true by every test using
        /// <see cref="OpenOutlineSettingsDialogFromSheetTabContextMenu"/> so an "unchanged" assertion
        /// can never pass vacuously because the callback silently never ran.
        /// </summary>
        public bool DialogWasFound { get; private set; }

        private MainWindowHarness(MainWindow window) => _window = window;

        public Workbook Workbook => _window.Session.Workbook;

        public SheetId CurrentSheetId => _window.CurrentSheetIdForTest;

        public void ActivateSheet(SheetId sheetId)
        {
            _window.SelectSingleSheetTabForTest(sheetId);
            _window.UpdateLayout();
            PumpDispatcher();
        }

        /// <summary>
        /// Right-clicks the named sheet tab (the real MouseRightButtonDown -&gt; context-menu-open
        /// path via RaiseSheetTabRightClickForTest), locates the "Outline Settings..." item by its
        /// localized header, and clicks it. That click synchronously opens a genuinely modal dialog
        /// via ShowDialog(); while it blocks and pumps the dispatcher, a queued callback finds it
        /// through the owner window's OwnedWindows (matched by its AutomationId, no test-only seam
        /// in production code), lets <paramref name="interact"/> drive it, and closes it.
        /// </summary>
        public void OpenOutlineSettingsDialogFromSheetTabContextMenu(string sheetName, Action<Window> interact)
        {
            var initialTarget = SheetTabTarget(sheetName);
            var mouseArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
            {
                RoutedEvent = UIElement.MouseRightButtonDownEvent,
                Source = initialTarget
            };
            _window.RaiseSheetTabRightClickForTest(initialTarget, mouseArgs);
            _window.UpdateLayout();
            PumpDispatcher();

            // SheetTab_MouseRightButtonDown calls RefreshSheetTabs(), which Clear()s/re-Add()s the
            // ObservableCollection the tab strip is bound to -- WPF regenerates every container from
            // that Reset, so the pre-click element is stale; re-resolve it now that it is current.
            var target = SheetTabTarget(sheetName);
            var contextMenu = target.ContextMenu
                ?? throw new InvalidOperationException($"Sheet tab '{sheetName}' has no context menu.");
            // Mirrors what the real WPF context-menu-open pipeline sets (and what the keyboard route,
            // TryOpenFocusedSheetTabContextMenu, sets explicitly): GetContextMenuTab(sender) resolves
            // the right-clicked tab via ContextMenu.PlacementTarget, which only a genuine popup-open
            // assigns -- so it must be set here too, or every click handler sees a null tab.
            contextMenu.PlacementTarget = target;
            var menuItem = contextMenu.Items
                .OfType<MenuItem>()
                .FirstOrDefault(item => string.Equals(
                    item.Header?.ToString(),
                    UiText.Get("OutlineSettings_MenuItem"),
                    StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    "No 'Outline Settings...' item on the WPF sheet-tab context menu -- freex-subtotals-outline-F2 regressed.");
            menuItem.IsEnabled.Should().BeTrue("Outline Settings has no workbook-state gating, unlike Delete/Hide/etc.");

            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = _window.OwnedWindows
                    .OfType<Window>()
                    .FirstOrDefault(w => string.Equals(
                        AutomationProperties.GetAutomationId(w),
                        "OutlineSettingsDialog",
                        StringComparison.Ordinal));
                if (dialog is null)
                    return;

                DialogWasFound = true;

                interact(dialog);

                if (dialog.IsVisible)
                    dialog.Close();
            }), DispatcherPriority.ApplicationIdle);

            menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            PumpDispatcher();
        }

        private FrameworkElement SheetTabTarget(string name)
        {
            if (_window.FindName("SheetTabsControl") is not ItemsControl tabs)
                throw new InvalidOperationException("SheetTabsControl not found.");

            return WpfTestTree.FindVisualDescendants<DependencyObject>(tabs)
                .Concat(WpfTestTree.FindLogicalDescendants<DependencyObject>(tabs))
                .OfType<FrameworkElement>()
                .Distinct()
                .Where(element =>
                    element.ContextMenu is not null &&
                    element.DataContext?.GetType().Name == "SheetTabViewModel")
                .Single(element => string.Equals(
                    (element.DataContext as SheetTabViewModel)?.Name,
                    name,
                    StringComparison.Ordinal));
        }

        public static MainWindowHarness Create(int sheetCount = 1)
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(graph, evaluator),
                [],
                workbookRef,
                workbook,
                NullUserMessageService.Instance)
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            window.Show();
            window.UpdateLayout();
            PumpDispatcher();

            var harness = new MainWindowHarness(window);
            for (var i = 1; i < sheetCount; i++)
            {
                window.InsertNewSheetForTest();
                window.UpdateLayout();
                PumpDispatcher();
            }

            return harness;
        }

        public void Dispose()
        {
            foreach (var owned in _window.OwnedWindows.OfType<Window>().ToArray())
                owned.Close();
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
            PumpDispatcher();
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
