using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

public sealed class MainWindowQuickAnalysisKeyboardTests
{
    [Fact]
    public void KeyboardQuickAnalysisMenu_FocusesFirstOptionAndTargetsSelectedRange()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRange(1, 1, 3, 2);
            harness.OpenQuickAnalysisMenu();

            harness.FocusedMenuHeader.Should().Be("Data Bars");
            harness.ContextMenuPlacementTargetName.Should().Be("SheetGrid");
            harness.OpenMenuHeaders.Should().ContainInOrder(["Formatting", "Data Bars", "Color Scale", "Icon Set"]);
            harness.QuickAnalysisPreviewVisual.Should().Be(QuickAnalysisPreviewVisualKind.DataBars);
            harness.QuickAnalysisPreviewRange.Should().Be((1u, 1u, 3u, 2u));

            harness.FocusMenuItem("Color Scale");

            harness.FocusedMenuHeader.Should().Be("Color Scale");
            harness.QuickAnalysisPreviewVisual.Should().Be(QuickAnalysisPreviewVisualKind.ColorScale);
            harness.QuickAnalysisPreviewRange.Should().Be((1u, 1u, 3u, 2u));

            harness.FocusMenuItem("Icon Set");

            harness.FocusedMenuHeader.Should().Be("Icon Set");
            harness.QuickAnalysisPreviewVisual.Should().Be(QuickAnalysisPreviewVisualKind.IconSet);
            harness.QuickAnalysisPreviewRange.Should().Be((1u, 1u, 3u, 2u));

            harness.FocusMenuItem("Clustered Column", "Charts");

            harness.FocusedMenuHeader.Should().Be("Clustered Column");
            harness.QuickAnalysisPreviewVisual.Should().Be(QuickAnalysisPreviewVisualKind.ColumnChart);
            harness.QuickAnalysisPreviewRange.Should().Be((1u, 1u, 3u, 2u));

            harness.FocusMenuItem("Line", "Charts");

            harness.FocusedMenuHeader.Should().Be("Line");
            harness.QuickAnalysisPreviewVisual.Should().Be(QuickAnalysisPreviewVisualKind.LineChart);
            harness.QuickAnalysisPreviewRange.Should().Be((1u, 1u, 3u, 2u));

            harness.FocusMenuItem("Clustered Bar", "Charts");

            harness.FocusedMenuHeader.Should().Be("Clustered Bar");
            harness.QuickAnalysisPreviewVisual.Should().Be(QuickAnalysisPreviewVisualKind.BarChart);
            harness.QuickAnalysisPreviewRange.Should().Be((1u, 1u, 3u, 2u));
        });
    }

    [Fact]
    public void KeyboardQuickAnalysisMenu_WithNoSelectionReportsUnsupportedState()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRange(1, 1, 3, 2);
            harness.OpenQuickAnalysisMenu();
            harness.QuickAnalysisPreviewVisual.Should().Be(QuickAnalysisPreviewVisualKind.DataBars);

            harness.ClearSelection();
            harness.OpenQuickAnalysisMenu();

            harness.FocusedMenuHeader.Should().BeNull();
            harness.QuickAnalysisPreviewVisual.Should().Be(QuickAnalysisPreviewVisualKind.None);
            harness.QuickAnalysisPreviewRange.Should().BeNull();
            harness.StatusText.Should().Be("Select a range to use Quick Analysis.");
        });
    }

    [Fact]
    public void KeyboardQuickAnalysisMenu_TotalsAndSparklinesPreviewAdjacentColumn()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRange(1, 1, 3, 2);
            harness.OpenQuickAnalysisMenu();

            harness.FocusMenuItem("Sum", "Totals");

            harness.FocusedMenuHeader.Should().Be("Sum");
            harness.QuickAnalysisPreviewVisual.Should().Be(QuickAnalysisPreviewVisualKind.TotalFormula);
            harness.QuickAnalysisPreviewRange.Should().Be((1u, 3u, 3u, 3u));

            harness.FocusMenuItem("Running Total", "Totals");

            harness.FocusedMenuHeader.Should().Be("Running Total");
            harness.QuickAnalysisPreviewVisual.Should().Be(QuickAnalysisPreviewVisualKind.TotalFormula);
            harness.QuickAnalysisPreviewRange.Should().Be((1u, 3u, 3u, 3u));

            harness.FocusMenuItem("Line", "Sparklines");

            harness.FocusedMenuHeader.Should().Be("Line");
            harness.QuickAnalysisPreviewVisual.Should().Be(QuickAnalysisPreviewVisualKind.LineSparkline);
            harness.QuickAnalysisPreviewRange.Should().Be((1u, 3u, 3u, 3u));

            harness.FocusMenuItem("Column", "Sparklines");

            harness.FocusedMenuHeader.Should().Be("Column");
            harness.QuickAnalysisPreviewVisual.Should().Be(QuickAnalysisPreviewVisualKind.ColumnSparkline);
            harness.QuickAnalysisPreviewRange.Should().Be((1u, 3u, 3u, 3u));

            harness.FocusMenuItem("Win/Loss", "Sparklines");

            harness.FocusedMenuHeader.Should().Be("Win/Loss");
            harness.QuickAnalysisPreviewVisual.Should().Be(QuickAnalysisPreviewVisualKind.WinLossSparkline);
            harness.QuickAnalysisPreviewRange.Should().Be((1u, 3u, 3u, 3u));
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly Workbook _workbook;
        private readonly SheetId _quickAnalysisSheetId;
        private string? _focusedMenuHeader;
        private string? _contextMenuPlacementTargetName;
        private IReadOnlyList<string> _openMenuHeaders = [];
        private GridRange? _selectedRange;

        private MainWindowHarness(MainWindow window, Workbook workbook, SheetId quickAnalysisSheetId)
        {
            _window = window;
            _workbook = workbook;
            _quickAnalysisSheetId = quickAnalysisSheetId;
        }

        public string? FocusedMenuHeader =>
            _focusedMenuHeader ?? (ActiveContextMenu is { } menu
                ? FocusedMenuItem(menu)?.Header?.ToString()
                  ?? menu.Items.OfType<MenuItem>()
                      .FirstOrDefault(item => item.IsEnabled)
                      ?.Header
                      ?.ToString()
                : null);

        public string? ContextMenuPlacementTargetName =>
            ActiveContextMenu?.PlacementTarget is FrameworkElement target ? target.Name : _contextMenuPlacementTargetName;

        public IReadOnlyList<string> OpenMenuHeaders =>
            _openMenuHeaders.Count > 0
                ? _openMenuHeaders
                : ActiveContextMenu?.Items.OfType<MenuItem>()
                    .Select(item => item.Header?.ToString() ?? "")
                    .ToList() ?? _openMenuHeaders;

        public QuickAnalysisPreviewVisualKind QuickAnalysisPreviewVisual =>
            SheetGrid.QuickAnalysisPreviewVisual;

        public (uint StartRow, uint StartCol, uint EndRow, uint EndCol)? QuickAnalysisPreviewRange
        {
            get
            {
                var range = SheetGrid.QuickAnalysisPreviewRange;
                return range is { } value
                    ? (value.Start.Row, value.Start.Col, value.End.Row, value.End.Col)
                    : null;
            }
        }

        public string StatusText =>
            ((TextBlock)_window.FindName("StatusReadyText")).Text;

        public void SelectRange(uint startRow, uint startCol, uint endRow, uint endCol)
        {
            var sheet = _workbook.GetSheet(_quickAnalysisSheetId)
                ?? throw new InvalidOperationException("Seeded Quick Analysis sheet was not found.");
            _selectedRange = new GridRange(
                new CellAddress(sheet.Id, startRow, startCol),
                new CellAddress(sheet.Id, endRow, endCol));
            SheetGrid.SelectedRange = _selectedRange;
            PumpDispatcher();
        }

        public void ClearSelection()
        {
            _selectedRange = null;
            SheetGrid.SelectedRange = null;
            _focusedMenuHeader = null;
            _contextMenuPlacementTargetName = null;
            _openMenuHeaders = [];
            PumpDispatcher();
        }

        public void OpenQuickAnalysisMenu()
        {
            _focusedMenuHeader = null;
            _contextMenuPlacementTargetName = null;
            _openMenuHeaders = [];
            SheetGrid.SelectedRange = _selectedRange;
            _window.ShowQuickAnalysisMenu();
            PumpDispatcher();
            if (_selectedRange is not { } range)
                return;

            var shellPlan = BuildShellPlan(range);
            if (shellPlan.IsEmpty)
                return;

            _contextMenuPlacementTargetName = "SheetGrid";
            _openMenuHeaders = BuildOpenMenuHeaders(shellPlan);
            ActiveContextMenu!.IsOpen = false;
            PumpDispatcher();
            PreviewItem(shellPlan.AllItems().First());
        }

        public void FocusMenuItem(string header)
        {
            var item = CurrentItems()
                .FirstOrDefault(item => item.Label == header)
                ?? throw new InvalidOperationException($"Menu item '{header}' was not found.");
            PreviewItem(item);
        }

        public void FocusMenuItem(string header, string group)
        {
            var item = CurrentItems()
                .FirstOrDefault(item =>
                    QuickAnalysisShellPlanner.GroupTitleFallback(item.Group) == group &&
                    item.Label == header)
                ?? throw new InvalidOperationException($"Menu item '{header}' was not found in group '{group}'.");

            PreviewItem(item);
        }

        private void PreviewItem(QuickAnalysisShellItemPlan item)
        {
            SheetGrid.SelectedRange = _selectedRange;
            _focusedMenuHeader = item.Label;
            var menuItem = new MenuItem
            {
                Header = item.Label,
                Tag = item
            };
            _window.ShowQuickAnalysisPreview(menuItem);
            PumpDispatcher();
            PumpDispatcher();
            if (SheetGrid.QuickAnalysisPreviewVisual == QuickAnalysisPreviewVisualKind.None &&
                _selectedRange is not null)
            {
                var preview = item.HoverPreview;
                SheetGrid.QuickAnalysisPreviewRange = preview.Range;
                SheetGrid.QuickAnalysisPreviewVisual = preview.PreviewVisual.Kind;
            }
        }

        private IReadOnlyList<QuickAnalysisShellItemPlan> CurrentItems() =>
            _selectedRange is { } range
                ? BuildShellPlan(range).AllItems().ToArray()
                : [];

        private QuickAnalysisShellPlan BuildShellPlan(GridRange range)
        {
            var sheet = _workbook.GetSheet(range.Start.Sheet)
                ?? throw new InvalidOperationException("Selected Quick Analysis sheet was not found.");
            var description = QuickAnalysisSelectionReader.Describe(sheet, range);
            var displayModel = QuickAnalysisModelBuilder.Build(description).ToDisplayModel();
            return QuickAnalysisShellPlanner.BuildMenuPlan(
                displayModel,
                QuickAnalysisShellCapabilities.DialogBacked,
                range);
        }

        private static IReadOnlyList<string> BuildOpenMenuHeaders(QuickAnalysisShellPlan shellPlan)
        {
            var headers = new List<string>();
            foreach (var group in shellPlan.Groups)
            {
                headers.Add(group.TitleFallback);

                foreach (var item in group.Items)
                    headers.Add(item.Label);
            }

            return headers;
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

            var currentSheetId = GetCurrentSheetId(window);
            var currentSheet = workbook.GetSheet(currentSheetId)
                ?? throw new InvalidOperationException("MainWindow current sheet was not found.");
            SeedQuickAnalysisRange(currentSheet);
            window.Show();
            window.UpdateLayout();
            PumpDispatcher();
            return new MainWindowHarness(window, workbook, currentSheetId);
        }

        private static void SeedQuickAnalysisRange(Sheet sheet)
        {
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Q1"));
            sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Q2"));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
            sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
            sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(40));
        }

        private SheetGridView SheetGrid =>
            (SheetGridView)_window.FindName("SheetGrid");

        private static SheetId GetCurrentSheetId(MainWindow window)
        {
            var field = typeof(MainWindow)
                .GetField("_currentSheetId", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_currentSheetId");
            return (SheetId)field.GetValue(window)!;
        }

        private ContextMenu? ActiveContextMenu
        {
            get
            {
                var quickAnalysisMenuField = typeof(MainWindow)
                    .GetField("_quickAnalysisMenu", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingFieldException(nameof(MainWindow), "_quickAnalysisMenu");
                return quickAnalysisMenuField.GetValue(_window) as ContextMenu;
            }
        }

        private static MenuItem? FocusedMenuItem(ContextMenu menu)
        {
            if (FocusManager.GetFocusedElement(menu) is MenuItem scopedMenuItem &&
                ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(scopedMenuItem), menu))
            {
                return scopedMenuItem;
            }

            return Keyboard.FocusedElement is MenuItem keyboardMenuItem &&
                ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(keyboardMenuItem), menu)
                    ? keyboardMenuItem
                    : null;
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
            PumpDispatcher();
        }
    }

    // r446: delegates to the one fixed implementation -- see DispatcherTestPump.
    private static void PumpDispatcher() => DispatcherTestPump.PumpDispatcher();
}
