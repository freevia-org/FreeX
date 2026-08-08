using System.Reflection;
using System.Windows;
using Free.Shared.AppServices;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R128-cellscmds-formatcells-activecell-1: the Format Cells dialog
/// (Ctrl+1, Ctrl+Shift+F, the Font/Number/Alignment/Border dialog-launcher arrows, and 'More
/// Number Formats…'/'More Borders…') must seed its fields from the TRUE active/anchor cell of
/// the selection (<see cref="FreeX.App.UI.GridView.ActiveCell"/>), not <c>SelectedRange</c>'s
/// normalized top-left <c>Start</c> corner -- those differ whenever the selection was extended
/// upward or leftward (e.g. click C5, then Shift+click A1, which keeps the active cell at C5 but
/// normalizes Start to A1). This is the same defect class already fixed for the Home-tab ribbon
/// toggles (R91-app-ribbon-state-5-1, see <see cref="R91_ToolbarVisualStateActiveCellTests"/>)
/// and for Ctrl+Enter/hyperlink-open (R112-model-active-cell-vs-selection-1-1).
///
/// Drives the real private <c>ResolveFormatCellsSeedCell</c> choke point -- the exact expression
/// <c>OpenFormatCellsDialog</c> uses to pick the cell whose style seeds the dialog -- via
/// reflection, rather than a hand-built model. <c>OpenFormatCellsDialog</c> itself cannot be
/// driven end-to-end in a headless xUnit run because it calls <c>FormatCellsDialog.ShowDialog()</c>,
/// which blocks on a real modal message pump; <c>ResolveFormatCellsSeedCell</c> is the nearest
/// genuine seam -- the actual production method, not a re-implementation of its logic.
/// </summary>
public sealed class R128_FormatCellsDialogActiveCellSeedTests
{
    private sealed class DocumentPlaceholderWindow(WorkbookId documentId) : IWorkbookWindow
    {
        public WorkbookId DocumentId { get; } = documentId;
        public void ApplyWindowTitleSuffix(string suffix) { }
        public void RefreshFromSharedWorkbook() { }
        public void RefreshTitleBar() { }
        public void ActivateWindow() { }
        public void SetWindowVisible(bool visible) { }
        public WorkbookScrollOffset GetScrollOffset() => default;
        public void SetScrollOffset(WorkbookScrollOffset offset) { }
        public void TileToWorkArea(Rect bounds) { }
        public void ApplyFormulaBarVisibility(bool visible) { }
        public void ApplySaveInProgress(bool inProgress) { }
    }

    private static (MainWindow Window, Workbook Workbook, Sheet Sheet) CreateAdoptedWindow()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var workbookRef = new WorkbookRef { Current = workbook };
        var registry = new WorkbookWindowRegistry();
        registry.Register(new DocumentPlaceholderWindow(workbook.Id));

        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
        var window = new MainWindow(
            NullLogger<MainWindow>.Instance,
            new ViewportService(),
            commandBus,
            new RecalcEngine(graph, evaluator),
            [],
            workbookRef,
            workbookRef.Current,
            NullUserMessageService.Instance,
            new WorkbookDocumentState(),
            windowRegistry: registry)
        {
            WindowState = WindowState.Normal,
            Width = 1280,
            Height = 720
        };

        window.Show();
        window.Activate();
        PumpDispatcher();

        return (window, workbook, sheet);
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static CellAddress InvokeResolveFormatCellsSeedCell(MainWindow window, GridRange range)
    {
        var method = typeof(MainWindow).GetMethod(
            "ResolveFormatCellsSeedCell", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (CellAddress)method!.Invoke(window, [range])!;
    }

    /// <summary>The scenario from the finding: click C5 (Bold), Shift+click A1 -- selecting
    /// A1:C5. The active cell must stay C5, not fall back to the range's normalized Start (A1),
    /// so the dialog's Font tab seeds Bold as checked -- matching what the ribbon already shows
    /// for this exact selection.</summary>
    [Fact]
    public void BackwardExtendedSelection_SeedsFromActiveCellNotStart() =>
        StaTestRunner.Run(() =>
    {
        var (window, workbook, sheet) = CreateAdoptedWindow();
        try
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var c5 = new CellAddress(sheet.Id, 5, 3);

            var boldStyleId = workbook.RegisterStyle(new CellStyle { Bold = true });
            sheet.SetCell(c5, new Cell { Value = new TextValue("active"), StyleId = boldStyleId });
            sheet.SetCell(a1, new Cell { Value = new TextValue("start") }); // default (not bold)

            // Click C5, then Shift+click A1: active cell stays C5, SelectedRange normalizes to A1:C5.
            window.SheetGrid.ActiveCell = c5;
            var range = new GridRange(c5, a1);
            window.SheetGrid.SelectedRange = range;

            range.Start.Should().Be(a1, "GridRange normalizes its Start to the top-left corner");

            var seedCell = InvokeResolveFormatCellsSeedCell(window, range);

            seedCell.Should().Be(c5,
                "the Format Cells dialog must seed from the ACTIVE cell (C5, bold), not range.Start (A1, not bold)");

            var seededStyle = workbook.GetStyle(sheet.GetCell(seedCell)!.StyleId);
            seededStyle.Bold.Should().BeTrue(
                "the style that would seed the dialog's Font tab must show Bold checked, matching the ribbon");
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });

    /// <summary>No-regression sibling: a forward (top-left-anchored) selection -- where ActiveCell
    /// and SelectedRange.Start coincide -- must keep seeding from Start exactly as before.</summary>
    [Fact]
    public void ForwardSelection_SeedsFromStartAsBefore() =>
        StaTestRunner.Run(() =>
    {
        var (window, workbook, sheet) = CreateAdoptedWindow();
        try
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var c5 = new CellAddress(sheet.Id, 5, 3);

            var boldStyleId = workbook.RegisterStyle(new CellStyle { Bold = true });
            sheet.SetCell(a1, new Cell { Value = new TextValue("active"), StyleId = boldStyleId });
            sheet.SetCell(c5, new Cell { Value = new TextValue("end") }); // default (not bold)

            // Click A1, then Shift+click C5: active cell stays A1, which is also SelectedRange.Start.
            window.SheetGrid.ActiveCell = a1;
            var range = new GridRange(a1, c5);
            window.SheetGrid.SelectedRange = range;

            range.Start.Should().Be(a1);

            var seedCell = InvokeResolveFormatCellsSeedCell(window, range);

            seedCell.Should().Be(a1, "the active cell A1 coincides with range.Start here");
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });

    /// <summary>No-active-cell fallback: if SheetGrid.ActiveCell is unset, resolution must still
    /// fall back to range.Start rather than throwing or seeding from nothing.</summary>
    [Fact]
    public void NoActiveCell_FallsBackToRangeStart() =>
        StaTestRunner.Run(() =>
    {
        var (window, _, sheet) = CreateAdoptedWindow();
        try
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var c5 = new CellAddress(sheet.Id, 5, 3);

            window.SheetGrid.ActiveCell = null;
            var range = new GridRange(a1, c5);

            var seedCell = InvokeResolveFormatCellsSeedCell(window, range);

            seedCell.Should().Be(range.Start);
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });
}
