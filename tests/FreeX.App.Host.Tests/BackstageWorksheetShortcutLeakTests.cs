using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression tests for round-140 finding "backstage-shortcuts-leak-to-worksheet"
/// (src/FreeX.App.Host/MainWindow.Selection.cs, guarded via IsStartScreenVisible()): while the
/// File-menu Backstage overlay is open, a worksheet keyboard shortcut (e.g. Delete/ClearSelection)
/// pressed with a Backstage rail button focused must NOT reach the hidden worksheet underneath.
/// </summary>
public sealed class BackstageWorksheetShortcutLeakTests
{
    [Fact]
    public void Delete_WhileBackstageOverlayOpenWithRailButtonFocused_DoesNotClearWorksheetCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = BackstageShortcutLeakHarness.Create();
            harness.SetCellValue(1, 1, new NumberValue(42));
            harness.SelectActiveCell(1, 1);

            harness.OpenBackstage();
            harness.FocusRailButton("BackstageHomeButton");
            harness.IsRailButtonFocused("BackstageHomeButton").Should()
                .BeTrue("the failure scenario requires a rail button -- not a TextBox/ComboBox -- to hold focus");

            // Real routed KeyDown, sourced from the actually-focused rail button (not a direct
            // reflection call into MainWindow_KeyDown): this exercises the genuine WPF bubble route
            // a real keypress takes -- rail Button -> ... -> BackstageFrame -> ... -> Window.
            harness.RaiseKeyDownFromFocusedElement(Key.Delete);

            harness.GetCellValue(1, 1).Should().BeOfType<NumberValue>(
                "Delete pressed while the Backstage overlay is open must not clear the hidden worksheet cell");
            harness.IsBackstageVisible.Should().BeTrue("the overlay itself must remain open -- Delete is not a Backstage command");
        });
    }

    [Fact]
    public void Delete_WithBackstageClosed_StillClearsWorksheetCell()
    {
        // Sibling/neighbouring-behavior guard: the fix must not disable Delete/ClearSelection for
        // the ordinary (Backstage-closed) case -- only while the overlay is actually visible.
        StaTestRunner.Run(() =>
        {
            using var harness = BackstageShortcutLeakHarness.Create();
            harness.SetCellValue(1, 1, new NumberValue(42));
            harness.SelectActiveCell(1, 1);
            harness.IsBackstageVisible.Should().BeFalse();

            harness.RaiseKeyDownFromFocusedElement(Key.Delete);

            harness.GetCellValue(1, 1).Should().Be(BlankValue.Instance,
                "Delete must still clear the active cell when Backstage is not open");
        });
    }

    private sealed class BackstageShortcutLeakHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MethodInfo _setActiveCell;
        private readonly MethodInfo _showStartScreen;
        private readonly object _backstageFrame;

        private BackstageShortcutLeakHarness(MainWindow window)
        {
            _window = window;
            _setActiveCell = typeof(MainWindow)
                .GetMethod("SetActiveCell", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SetActiveCell");
            _showStartScreen = typeof(MainWindow)
                .GetMethod("ShowStartScreen", BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)
                ?? throw new MissingMethodException(nameof(MainWindow), "ShowStartScreen");
            _backstageFrame = typeof(MainWindow)
                .GetField("_backstageFrame", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(window)
                ?? throw new InvalidOperationException("MainWindow did not build a BackstageFrame.");
        }

        private Workbook LiveWorkbook => _window.Session.Workbook;

        private Sheet Sheet => LiveWorkbook.Sheets[0];

        private SheetId SheetId => Sheet.Id;

        private SheetGridView Grid => (SheetGridView)_window.FindName("SheetGrid");

        public bool IsBackstageVisible =>
            ((UIElement)_window.FindName("StartScreenOverlay")).Visibility == Visibility.Visible;

        public void SetCellValue(uint row, uint col, ScalarValue value)
        {
            Sheet.SetCell(new CellAddress(SheetId, row, col), value);
            PumpDispatcher();
        }

        public ScalarValue GetCellValue(uint row, uint col) =>
            Sheet.GetCell(new CellAddress(SheetId, row, col))?.Value ?? BlankValue.Instance;

        public void SelectActiveCell(uint row, uint col)
        {
            _setActiveCell.Invoke(_window, [new CellAddress(SheetId, row, col)]);
            PumpDispatcher();
        }

        public void OpenBackstage()
        {
            _window.Activate();
            _showStartScreen.Invoke(_window, null);
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public void FocusRailButton(string automationId)
        {
            var button = RailButtons()
                .FirstOrDefault(candidate => AutomationProperties.GetAutomationId(candidate) == automationId)
                ?? throw new InvalidOperationException($"Rail button '{automationId}' not found.");
            _window.Activate();
            FocusManager.SetFocusedElement(_window, button);
            button.Focus();
            Keyboard.Focus(button);
            PumpDispatcher();
        }

        public bool IsRailButtonFocused(string automationId) =>
            ReferenceEquals(
                Keyboard.FocusedElement,
                RailButtons().FirstOrDefault(button => AutomationProperties.GetAutomationId(button) == automationId));

        /// <summary>
        /// Raise a real, routed WPF KeyDown starting at whatever element currently holds keyboard
        /// focus -- e.g. the Backstage rail button FocusRailButton just focused -- and let it bubble
        /// exactly as it would for a real keypress (through BackstageFrame, StartScreenOverlay,
        /// RootGrid, up to the Window's own MainWindow_KeyDown handler).
        /// </summary>
        public void RaiseKeyDownFromFocusedElement(Key key)
        {
            var focused = Keyboard.FocusedElement as UIElement
                ?? throw new InvalidOperationException("No element currently holds keyboard focus.");
            var source = PresentationSource.FromVisual(focused)
                ?? throw new InvalidOperationException("Focused element has no presentation source.");
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };
            focused.RaiseEvent(args);
            PumpDispatcher();
        }

        private IReadOnlyList<Button> RailButtons() =>
            Descendants(_backstageFrame as DependencyObject)
                .OfType<Button>()
                .Where(button => AutomationProperties.GetAutomationId(button).StartsWith("Backstage", StringComparison.Ordinal))
                .ToList();

        private static IEnumerable<DependencyObject> Descendants(DependencyObject? root)
        {
            if (root is null)
                yield break;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var descendant in Descendants(child))
                    yield return descendant;
            }
        }

        public static BackstageShortcutLeakHarness Create()
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
                Array.Empty<FreeX.Core.IO.IFileAdapter>(),
                workbookRef,
                workbook,
                NullUserMessageService.Instance)
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            window.Show();
            window.Activate();
            window.UpdateLayout();
            PumpDispatcher();

            // MainWindow_Loaded replaces the constructor's workbook via CreateNewWorkbook(), so the
            // harness must be constructed (and cells populated) against the LIVE post-Loaded sheet.
            return new BackstageShortcutLeakHarness(window);
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
            PumpDispatcher();
        }

        private static void PumpDispatcher()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
    }
}
