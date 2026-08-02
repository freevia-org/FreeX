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
/// Regression coverage for R90-app-window-arrange-freeze-ui-5-2: View &gt; New Window must open the
/// new window on the INVOKING window's current sheet (Excel: New Window opens as a copy of the
/// window it was invoked from), not on whichever sibling window happens to be first in the
/// registry's registration order. The bug only surfaces with 3+ windows sharing a document whose
/// sheets have been independently navigated -- with only two windows, "first other MainWindow in
/// the registry" and "the invoking window" are the same window, masking the defect.
/// </summary>
public sealed class R90_NewWindowSourceHintSheetResolutionTests
{
    /// <summary>
    /// A minimal <see cref="IWorkbookWindow"/> fake used purely to make the registry report
    /// "a window already views this document" so the first real <see cref="MainWindow"/> we
    /// construct adopts the shared workbook (via <c>AdoptSharedWorkbook</c>) instead of replacing
    /// it with a fresh one (<c>MainWindow_Loaded</c>'s default <c>CreateNewWorkbook()</c> path) --
    /// letting the test seed a specific multi-sheet workbook and keep it alive across the first
    /// window's Show()/Loaded cycle.
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

    private static void SelectSheetTab(MainWindow window, SheetId sheetId)
    {
        var method = typeof(MainWindow).GetMethod("SelectSingleSheetTab", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(window, [sheetId]);
    }

    private static SheetId GetCurrentSheetId(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_currentSheetId", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return (SheetId)field!.GetValue(window)!;
    }

    private static void SetNewWindowSourceHint(MainWindow newWindow, MainWindow source) =>
        newWindow.SetNewWindowSourceHint(source);

    [Fact]
    public void ThirdWindow_OpensOnInvokingWindowsSheet_NotTheFirstRegisteredSiblingsSheet() =>
        StaTestRunner.Run(() =>
    {
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var workbookRef = new WorkbookRef { Current = workbook };
        var registry = new WorkbookWindowRegistry();
        var documentState = new WorkbookDocumentState();

        // Pre-register a placeholder so window A adopts our seeded multi-sheet workbook instead of
        // MainWindow_Loaded replacing it with a fresh one.
        registry.Register(new DocumentPlaceholderWindow(workbook.Id));

        // Window A: opens on Sheet1 (the adopt-fallback, since the only other "window" isn't a
        // MainWindow) and stays there -- this is the window Excel calls "Book1:1".
        var windowA = CreateWindow(workbookRef, registry, documentState);
        windowA.Show();
        windowA.Activate();
        PumpDispatcher();
        GetCurrentSheetId(windowA).Should().Be(workbook.Sheets[0].Id);

        // Window B: View > New Window from A -- adopts A's current sheet (Sheet1), matching Excel.
        var windowB = CreateWindow(workbookRef, registry, documentState);
        SetNewWindowSourceHint(windowB, windowA);
        windowB.Show();
        windowB.Activate();
        PumpDispatcher();
        GetCurrentSheetId(windowB).Should().Be(workbook.Sheets[0].Id);

        // User switches B (independently) to Sheet2 -- the "per-window active sheet is
        // independently navigable" feature this bug report is about.
        SelectSheetTab(windowB, sheet2.Id);
        GetCurrentSheetId(windowB).Should().Be(sheet2.Id);

        try
        {
            // Registry registration order right now: [placeholder, A(Sheet1), B(Sheet2)]. The OLD
            // ResolveAdoptedSheetId loop would find A first (registration order) and open C on
            // Sheet1 -- wrong. With the invoking-window hint set (as the fixed ViewNewWindowBtn_Click
            // now does), C must open on Sheet2, since it was invoked from B.
            var windowC = CreateWindow(workbookRef, registry, documentState);
            SetNewWindowSourceHint(windowC, windowB);
            windowC.Show();
            windowC.Activate();
            PumpDispatcher();

            GetCurrentSheetId(windowC).Should().Be(
                sheet2.Id,
                "Excel opens New Window as a copy of the window it was invoked from (B, on Sheet2), " +
                "not the first sibling in registration order (A, on Sheet1)");

            MainWindowTestCleanup.CloseWithoutSavePrompt(windowC);
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(windowB);
            MainWindowTestCleanup.CloseWithoutSavePrompt(windowA);
            PumpDispatcher();
        }
    });

    /// <summary>
    /// No-regression sibling: when no invoking-window hint is set at all (e.g. a hypothetical
    /// future caller that constructs a secondary window without going through the fixed
    /// ViewNewWindowBtn_Click), resolution must still fall back to the pre-existing
    /// "first other MainWindow in registration order" behavior rather than throwing or defaulting
    /// straight to Sheets[0].
    /// </summary>
    [Fact]
    public void WithoutASourceHint_FallsBackToFirstRegisteredSiblingsSheet() =>
        StaTestRunner.Run(() =>
    {
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var workbookRef = new WorkbookRef { Current = workbook };
        var registry = new WorkbookWindowRegistry();
        var documentState = new WorkbookDocumentState();

        registry.Register(new DocumentPlaceholderWindow(workbook.Id));

        var windowA = CreateWindow(workbookRef, registry, documentState);
        windowA.Show();
        windowA.Activate();
        PumpDispatcher();
        SelectSheetTab(windowA, sheet2.Id);
        GetCurrentSheetId(windowA).Should().Be(sheet2.Id);

        try
        {
            // No SetNewWindowSourceHint call this time -- must still resolve to A's sheet via the
            // registry-order fallback, exactly like before this fix.
            var windowB = CreateWindow(workbookRef, registry, documentState);
            windowB.Show();
            windowB.Activate();
            PumpDispatcher();

            GetCurrentSheetId(windowB).Should().Be(sheet2.Id);

            MainWindowTestCleanup.CloseWithoutSavePrompt(windowB);
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(windowA);
            PumpDispatcher();
        }
    });
}
