using System.Reflection;
using System.Windows;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for the MED finding: View &gt; New Window always opened the sibling at cell
/// A1 instead of copying the invoking window's active cell/selection. <c>AdoptSharedWorkbook</c>
/// (MainWindow.MultiWindow.cs) correctly resolves the new sibling onto the invoking window's current
/// SHEET via <c>ResolveAdoptedSheetId</c> (R90), but unconditionally reset the active cell to A1,
/// ignoring both <c>_newWindowSourceHint</c>'s current active cell/selection and the shared
/// <c>Sheet.ActiveRow</c>/<c>ActiveCol</c> fields that the ordinary File &gt; Open path
/// (<c>ApplyOpenedWorksheetViewState</c>) already uses for exactly this purpose. Excel's View &gt;
/// New Window opens the new window as a live duplicate of the invoking window -- same sheet, same
/// active cell, same selection -- the two windows only diverge once the user navigates one
/// independently.
/// </summary>
public sealed class R120_NewWindowSeedsActiveCellAndSelectionTests
{
    /// <summary>
    /// A minimal <see cref="IWorkbookWindow"/> fake used purely to make the registry report
    /// "a window already views this document" so the first real <see cref="MainWindow"/> we
    /// construct adopts the shared workbook (via <c>AdoptSharedWorkbook</c>) instead of replacing
    /// it with a fresh one, matching the pattern used by
    /// <see cref="R90_NewWindowSourceHintSheetResolutionTests"/>.
    /// </summary>
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

    private static MainWindow CreateWindow(
        WorkbookRef workbookRef,
        WorkbookWindowRegistry registry,
        WorkbookDocumentState documentState)
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
        return new MainWindow(
            NullLogger<MainWindow>.Instance,
            new ViewportService(),
            commandBus,
            new RecalcEngine(graph, evaluator),
            [],
            workbookRef,
            workbookRef.Current,
            NullUserMessageService.Instance,
            documentState,
            windowRegistry: registry)
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

    /// <summary>Drives the same private selection setter the real grid/Name Box/Go To use.</summary>
    private static void SetSelectionRange(MainWindow window, GridRange range, CellAddress activeCell)
    {
        var method = typeof(MainWindow).GetMethod("SetSelectionRange", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(window, [range, activeCell]);
    }

    private static void SetNewWindowSourceHint(MainWindow newWindow, MainWindow source) =>
        newWindow.SetNewWindowSourceHint(source);

    [Fact]
    public void NewWindow_CopiesInvokingWindowsActiveCellAndRangeSelection_InsteadOfResettingToA1() =>
        StaTestRunner.Run(() =>
    {
        var workbook = new Workbook("Book1");
        var sheet1 = workbook.AddSheet("Sheet1");
        var workbookRef = new WorkbookRef { Current = workbook };
        var registry = new WorkbookWindowRegistry();
        var documentState = new WorkbookDocumentState();

        registry.Register(new DocumentPlaceholderWindow(workbook.Id));

        // Window A: the invoking window. User selects a large range anchored on Z100 (the
        // repro from the bug report), extending to AA110.
        var windowA = CreateWindow(workbookRef, registry, documentState);
        windowA.Show();
        windowA.Activate();
        PumpDispatcher();

        var anchor = new CellAddress(sheet1.Id, 100, 26);   // Z100
        var cursor = new CellAddress(sheet1.Id, 110, 27);   // AA110
        var expectedRange = new GridRange(anchor, cursor);
        SetSelectionRange(windowA, expectedRange, anchor);

        windowA.SheetGrid.SelectedRange.Should().Be(expectedRange);
        windowA.SheetGrid.ActiveCell.Should().Be(anchor);

        try
        {
            // Window B: View > New Window from A -- must open as a live duplicate of A's current
            // state: same sheet (already covered by R90), same active cell, same range selection.
            var windowB = CreateWindow(workbookRef, registry, documentState);
            SetNewWindowSourceHint(windowB, windowA);
            windowB.Show();
            windowB.Activate();
            PumpDispatcher();

            windowB.SheetGrid.SelectedRange.Should().Be(
                expectedRange,
                "Excel's View > New Window duplicates the invoking window's current selection, " +
                "not just its sheet");
            windowB.SheetGrid.ActiveCell.Should().Be(
                anchor,
                "the new window's active cell must match the invoking window's active cell (Z100), " +
                "not reset to A1");

            MainWindowTestCleanup.CloseWithoutSavePrompt(windowB);
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(windowA);
            PumpDispatcher();
        }
    });

    /// <summary>
    /// No-regression sibling: when no invoking-window hint is usable (mirrors
    /// <see cref="R90_NewWindowSourceHintSheetResolutionTests.WithoutASourceHint_FallsBackToFirstRegisteredSiblingsSheet"/>),
    /// the adopted window must still fall back cleanly to plain A1 when the resolved sheet has no
    /// remembered <see cref="Sheet.ActiveRow"/>/<see cref="Sheet.ActiveCol"/> -- the pre-existing
    /// default this fix must not disturb -- rather than crashing or inheriting stale selection state
    /// from an unrelated window.
    /// </summary>
    [Fact]
    public void WithoutSourceHint_NoRememberedActiveCell_StillDefaultsToA1() =>
        StaTestRunner.Run(() =>
    {
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1");
        var workbookRef = new WorkbookRef { Current = workbook };
        var registry = new WorkbookWindowRegistry();
        var documentState = new WorkbookDocumentState();

        registry.Register(new DocumentPlaceholderWindow(workbook.Id));

        var windowA = CreateWindow(workbookRef, registry, documentState);
        windowA.Show();
        windowA.Activate();
        PumpDispatcher();

        try
        {
            // No SetNewWindowSourceHint call -- resolves via the pre-existing registry-order
            // fallback (R90), and Sheet1 has no remembered ActiveRow/ActiveCol, so the new window
            // must default to A1, exactly like before this fix.
            var windowB = CreateWindow(workbookRef, registry, documentState);
            windowB.Show();
            windowB.Activate();
            PumpDispatcher();

            var expectedA1 = new GridRange(
                new CellAddress(workbook.Sheets[0].Id, 1, 1),
                new CellAddress(workbook.Sheets[0].Id, 1, 1));
            windowB.SheetGrid.SelectedRange.Should().Be(expectedA1);
            windowB.SheetGrid.ActiveCell.Should().Be(new CellAddress(workbook.Sheets[0].Id, 1, 1));

            MainWindowTestCleanup.CloseWithoutSavePrompt(windowB);
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(windowA);
            PumpDispatcher();
        }
    });
}
