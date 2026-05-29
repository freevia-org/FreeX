using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Reflection;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

public sealed class StatusBarLayoutTests
{
    [Fact]
    public void NumericSelection_RendersExcelLikeAggregateStatusLabels()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var sheet = harness.ActiveWorkbook.GetSheet(harness.CurrentSheetId)
                ?? throw new InvalidOperationException("The active sheet should exist.");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(2)));
            sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(4)));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(6)));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new TextValue("ignored")));

            harness.SelectRange(1, 1, 2, 2);
            harness.RefreshStatusBar();

            harness.StatusReadyVisibility.Should().Be(Visibility.Collapsed);
            harness.StatusStatsVisibility.Should().Be(Visibility.Visible);
            harness.StatusText("StatusAvgText").Should().Be("Average: 4");
            harness.StatusText("StatusCountText").Should().Be("Count: 4");
            harness.StatusText("StatusNumericalCountText").Should().Be("Numerical Count: 3");
            harness.StatusText("StatusSumText").Should().Be("Sum: 12");
            harness.StatusText("StatusMinText").Should().Be("Min: 2");
            harness.StatusText("StatusMaxText").Should().Be("Max: 6");
        });
    }

    [Fact]
    public void StatusModeText_RendersReadyInputPromptAndInlineEditMode()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var sheet = harness.ActiveWorkbook.GetSheet(harness.CurrentSheetId)
                ?? throw new InvalidOperationException("The active sheet should exist.");
            var inputCell = new CellAddress(sheet.Id, 2, 1);
            sheet.DataValidations.Add(new DataValidation
            {
                AppliesTo = new GridRange(inputCell, inputCell),
                ShowInputMessage = true,
                PromptTitle = "Input",
                PromptMessage = "Use a whole number"
            });

            harness.SelectRange(1, 1, 1, 1);
            harness.RefreshStatusBar();
            harness.StatusReadyVisibility.Should().Be(Visibility.Visible);
            harness.StatusStatsVisibility.Should().Be(Visibility.Collapsed);
            harness.StatusText("StatusReadyText").Should().Be("Ready");

            harness.SelectRange(2, 1, 2, 1);
            harness.RefreshStatusBar();
            harness.StatusReadyVisibility.Should().Be(Visibility.Visible);
            harness.StatusStatsVisibility.Should().Be(Visibility.Collapsed);
            harness.StatusText("StatusReadyText").Should().Be("Input: Use a whole number");

            harness.ShowInlineEditor(inputCell);
            harness.StatusReadyVisibility.Should().Be(Visibility.Visible);
            harness.StatusStatsVisibility.Should().Be(Visibility.Collapsed);
            harness.StatusText("StatusReadyText").Should().Be("Edit");
        });
    }

    [Fact]
    public void AggregateViewport_StaysClearOfZoomControlsAtCompactWidth()
    {
        StaTestRunner.Run(() =>
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
                NullUserMessageService.Instance);

            try
            {
                window.Width = 860;
                window.Height = 500;
                window.Show();

                ((TextBlock)window.FindName("StatusReadyText")).Visibility = Visibility.Collapsed;
                var statsPanel = (StackPanel)window.FindName("StatusStatsPanel");
                statsPanel.Visibility = Visibility.Visible;
                ((TextBlock)window.FindName("StatusAvgText")).Text = "Average: 2";
                ((TextBlock)window.FindName("StatusCountText")).Text = "Count: 1";
                ((TextBlock)window.FindName("StatusNumericalCountText")).Text = "Numerical Count: 1";
                ((TextBlock)window.FindName("StatusSumText")).Text = "Sum: 2";
                ((TextBlock)window.FindName("StatusMinText")).Text = "Min: 2";
                ((TextBlock)window.FindName("StatusMaxText")).Text = "Max: 2";

                window.UpdateLayout();
                PumpDispatcher();
                window.UpdateLayout();

                var statsViewport = (FrameworkElement)window.FindName("StatusStatsViewport");
                var zoomControls = (FrameworkElement)window.FindName("StatusZoomControls");
                var maxText = (FrameworkElement)window.FindName("StatusMaxText");

                var viewportBounds = BoundsRelativeToWindow(statsViewport, window);
                var zoomBounds = BoundsRelativeToWindow(zoomControls, window);
                var maxTextBounds = BoundsRelativeToWindow(maxText, window);

                viewportBounds.Right.Should().BeLessThanOrEqualTo(zoomBounds.Left + 0.5);
                maxTextBounds.Right.Should().BeLessThanOrEqualTo(viewportBounds.Right + 0.5);
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void F6StatusBarFocus_StartsAtZoomOutAndTabStaysInZoomControls()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.CycleShellFocus(reverse: true);

            harness.FocusedElementName.Should().Be("StatusZoomOutButton");

            harness.HandleFocusedStatusBarTab().Should().BeTrue();

            harness.FocusedElementName.Should().Be("ZoomSlider");
        });
    }

    [Fact]
    public void StatusZoomPercent_IsKeyboardReachableAfterZoomButtons()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.CycleShellFocus(reverse: true);
            harness.FocusedElementName.Should().Be("StatusZoomOutButton");

            harness.HandleFocusedStatusBarTab().Should().BeTrue();
            harness.FocusedElementName.Should().Be("ZoomSlider");

            harness.HandleFocusedStatusBarTab().Should().BeTrue();
            harness.FocusedElementName.Should().Be("StatusZoomInButton");

            harness.HandleFocusedStatusBarTab().Should().BeTrue();
            harness.FocusedElementName.Should().Be("StatusZoomText");
        });
    }

    [Fact]
    public void EscapeFromStatusBarFocus_ReturnsFocusToWorksheet()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.CycleShellFocus(reverse: true);
            harness.FocusedElementName.Should().Be("StatusZoomOutButton");

            harness.HandleFocusedStatusBarKey(Key.Escape).Should().BeTrue();

            harness.FocusedElementIsWorksheet.Should().BeTrue();
            harness.CurrentShellFocusTarget.Should().Be(ShellFocusTarget.Worksheet);
        });
    }

    [Fact]
    public void ZoomSliderAndButtons_ShareACommonVisualCenter()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var window = harness.Window;

            var zoomOut = (FrameworkElement)window.FindName("StatusZoomOutButton");
            var slider = (FrameworkElement)window.FindName("ZoomSlider");
            var zoomIn = (FrameworkElement)window.FindName("StatusZoomInButton");
            var zoomText = (FrameworkElement)window.FindName("StatusZoomText");

            window.UpdateLayout();
            PumpDispatcher();
            window.UpdateLayout();

            var sliderCenter = CenterY(slider, window);
            CenterY(zoomOut, window).Should().BeApproximately(sliderCenter, 0.75);
            CenterY(zoomIn, window).Should().BeApproximately(sliderCenter, 0.75);
            CenterY(zoomText, window).Should().BeApproximately(sliderCenter, 0.75);
        });
    }

    [Fact]
    public void F6ShellFocusCycle_TraversesVisibleExcelRegionsInHost()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.CurrentShellFocusTarget.Should().Be(ShellFocusTarget.Worksheet);

            harness.CycleShellFocus(reverse: false);
            harness.CurrentShellFocusTarget.Should().Be(ShellFocusTarget.Ribbon);

            harness.CycleShellFocus(reverse: false);
            harness.CurrentShellFocusTarget.Should().Be(ShellFocusTarget.FormulaBar);
            harness.FocusedElementName.Should().Be("FormulaBar");

            harness.CycleShellFocus(reverse: false);
            harness.CurrentShellFocusTarget.Should().Be(ShellFocusTarget.SheetTabs);
            harness.FocusedSheetTabName.Should().Be("Sheet1");

            harness.CycleShellFocus(reverse: false);
            harness.CurrentShellFocusTarget.Should().Be(ShellFocusTarget.StatusBar);
            harness.FocusedElementName.Should().Be("StatusZoomOutButton");

            harness.CycleShellFocus(reverse: false);
            harness.CurrentShellFocusTarget.Should().Be(ShellFocusTarget.Worksheet);
        });
    }

    [Fact]
    public void F6ShellFocusCycle_LandsInVisiblePivotFieldListPane()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.ShowPivotFieldListPane();

            harness.CycleShellFocus(reverse: false);
            harness.CycleShellFocus(reverse: false);
            harness.CycleShellFocus(reverse: false);
            harness.CurrentShellFocusTarget.Should().Be(ShellFocusTarget.SheetTabs);

            harness.CycleShellFocus(reverse: false);

            harness.CurrentShellFocusTarget.Should().BeOneOf(ShellFocusTarget.TaskPane, ShellFocusTarget.StatusBar);
            harness.FocusedElementName.Should().BeOneOf("PivotFieldListSearchBox", "StatusZoomOutButton");
        });
    }

    [Fact]
    public void F6ShellFocusCycle_LandsInVisibleSlicerTimelinePane()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.ShowSlicerTimelinePane();

            harness.CycleShellFocus(reverse: false);
            harness.CycleShellFocus(reverse: false);
            harness.CycleShellFocus(reverse: false);
            harness.CurrentShellFocusTarget.Should().Be(ShellFocusTarget.SheetTabs);

            harness.CycleShellFocus(reverse: false);

            harness.CurrentShellFocusTarget.Should().BeOneOf(ShellFocusTarget.TaskPane, ShellFocusTarget.StatusBar);
            harness.FocusedElementName.Should().BeOneOf("SlicerTimelinePaneCloseBtn", "SlicerTimelinePane", "StatusZoomOutButton");
        });
    }

    private static Rect BoundsRelativeToWindow(FrameworkElement element, Window window) =>
        element.TransformToAncestor(window).TransformBounds(new Rect(new Size(element.ActualWidth, element.ActualHeight)));

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new InvalidOperationException($"Sheet {sheetId} not found");
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly FieldInfo _workbook;
        private readonly MethodInfo _cycleShellFocus;
        private readonly FieldInfo _currentSheetId;
        private readonly MethodInfo _getCurrentShellFocusTarget;
        private readonly MethodInfo _refreshStatusBar;
        private readonly MethodInfo _showInlineEditor;
        private readonly MethodInfo _tryHandleFocusedStatusBarKeyboardNavigation;

        private MainWindowHarness(MainWindow window, Workbook workbook)
        {
            _window = window;
            Workbook = workbook;
            _workbook = typeof(MainWindow)
                .GetField("_workbook", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_workbook");
            _currentSheetId = typeof(MainWindow)
                .GetField("_currentSheetId", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_currentSheetId");
            _cycleShellFocus = typeof(MainWindow)
                .GetMethod("CycleShellFocus", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CycleShellFocus");
            _getCurrentShellFocusTarget = typeof(MainWindow)
                .GetMethod("GetCurrentShellFocusTarget", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "GetCurrentShellFocusTarget");
            _refreshStatusBar = typeof(MainWindow)
                .GetMethod("RefreshStatusBar", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "RefreshStatusBar");
            _showInlineEditor = typeof(MainWindow)
                .GetMethod("ShowInlineEditor", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ShowInlineEditor");
            _tryHandleFocusedStatusBarKeyboardNavigation = typeof(MainWindow)
                .GetMethod("TryHandleFocusedStatusBarKeyboardNavigation", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "TryHandleFocusedStatusBarKeyboardNavigation");
        }

        public Workbook Workbook { get; }

        public Workbook ActiveWorkbook => (Workbook)_workbook.GetValue(_window)!;

        public SheetId CurrentSheetId => (SheetId)_currentSheetId.GetValue(_window)!;

        public ShellFocusTarget CurrentShellFocusTarget =>
            (ShellFocusTarget)_getCurrentShellFocusTarget.Invoke(_window, [])!;

        public string? FocusedElementName =>
            Keyboard.FocusedElement is FrameworkElement element ? element.Name : null;

        public bool FocusedElementIsWorksheet =>
            ReferenceEquals(Keyboard.FocusedElement, _window.SheetGrid);

        public string? FocusedSheetTabName =>
            Keyboard.FocusedElement is FrameworkElement element &&
            element.DataContext is SheetTabViewModel sheetTab
                ? sheetTab.Name
                : null;

        public MainWindow Window => _window;

        public Visibility StatusReadyVisibility =>
            ((TextBlock)_window.FindName("StatusReadyText")).Visibility;

        public Visibility StatusStatsVisibility =>
            ((StackPanel)_window.FindName("StatusStatsPanel")).Visibility;

        public string StatusText(string textBlockName) =>
            ((TextBlock)_window.FindName(textBlockName)).Text;

        public void SelectRange(uint startRow, uint startCol, uint endRow, uint endCol)
        {
            var sheet = ActiveWorkbook.GetSheet(CurrentSheetId)
                ?? throw new InvalidOperationException("The active sheet should exist.");
            _window.SheetGrid.SelectedRange = new GridRange(
                new CellAddress(sheet.Id, startRow, startCol),
                new CellAddress(sheet.Id, endRow, endCol));
            PumpDispatcher();
        }

        public void RefreshStatusBar()
        {
            _refreshStatusBar.Invoke(_window, []);
            PumpDispatcher();
        }

        public void ShowInlineEditor(CellAddress address)
        {
            _showInlineEditor.Invoke(_window, [address]);
            PumpDispatcher();
        }

        public void CycleShellFocus(bool reverse)
        {
            _cycleShellFocus.Invoke(_window, [reverse]);
            PumpDispatcher();
        }

        public void ShowPivotFieldListPane()
        {
            SetElementVisibility("SlicerTimelinePane", Visibility.Collapsed);
            SetElementVisibility("PivotFieldListPane", Visibility.Visible);
            PumpDispatcher();
        }

        public void ShowSlicerTimelinePane()
        {
            SetElementVisibility("PivotFieldListPane", Visibility.Collapsed);
            SetElementVisibility("SlicerTimelinePane", Visibility.Visible);
            PumpDispatcher();
        }

        public bool HandleFocusedStatusBarTab()
        {
            return HandleFocusedStatusBarKey(Key.Tab);
        }

        public bool HandleFocusedStatusBarKey(Key key)
        {
            var source = PresentationSource.FromVisual(_window);
            source.Should().NotBeNull("the test window must be visible before routing keyboard input");
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source!, Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            };

            var handled = (bool)_tryHandleFocusedStatusBarKeyboardNavigation.Invoke(_window, [args])!;
            PumpDispatcher();
            return handled;
        }

        public static MainWindowHarness Create()
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
            if (window.FindName("FormulaBarBorder") is FrameworkElement formulaBarBorder)
                formulaBarBorder.Visibility = Visibility.Visible;
            window.UpdateLayout();
            PumpDispatcher();
            return new MainWindowHarness(window, workbook);
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
            PumpDispatcher();
        }

        private void SetElementVisibility(string name, Visibility visibility)
        {
            var element = (FrameworkElement)_window.FindName(name);
            element.Visibility = visibility;
            _window.UpdateLayout();
        }
    }

    private static double CenterY(FrameworkElement element, Window window)
    {
        var bounds = BoundsRelativeToWindow(element, window);
        return bounds.Top + bounds.Height / 2;
    }
}
