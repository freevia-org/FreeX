using System.Reflection;
using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.Ribbon;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R91-app-ribbon-state-5-1: the Bold/Italic/Underline/alignment/Wrap-Text
/// ribbon toggles must reflect the TRUE active/anchor cell of the selection (<see
/// cref="FreeX.App.UI.GridView.ActiveCell"/>), not <c>SelectedRange</c>'s normalized top-left
/// <c>Start</c> corner -- those differ whenever the selection was extended upward or leftward (e.g.
/// click C3, then Shift+click A1, which keeps the active cell at C3 but normalizes Start to A1).
/// Drives the real private <c>RefreshToolbarVisualState</c> choke point via reflection so the test
/// exercises the actual fixed code path, not a hand-built model.
/// </summary>
public sealed class R91_ToolbarVisualStateActiveCellTests
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

    /// <summary>Sets up a backward-extended selection (anchor at C3, extended to A1) and drives
    /// the real private RefreshToolbarVisualState via reflection.</summary>
    private static void SelectBackwardExtendedRangeAndRefreshToolbar(
        MainWindow window, CellAddress anchor, CellAddress extendTo)
    {
        var anchorField = typeof(MainWindow).GetField(
            "_selectionAnchorField", BindingFlags.Instance | BindingFlags.NonPublic);
        anchorField.Should().NotBeNull();
        anchorField!.SetValue(window, anchor);

        window.SheetGrid.ActiveCell = anchor;
        window.SheetGrid.SelectedRange = new GridRange(anchor, extendTo);

        var refreshMethod = typeof(MainWindow).GetMethod(
            "RefreshToolbarVisualState", BindingFlags.Instance | BindingFlags.NonPublic);
        refreshMethod.Should().NotBeNull();
        refreshMethod!.Invoke(window, null);
    }

    private static bool GetRibbonChecked(MainWindow window, string commandId)
    {
        var field = typeof(MainWindow).GetField("_ribbonState", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        var store = (IRibbonStateStore)field!.GetValue(window)!;
        return store.GetState(commandId).IsChecked;
    }

    [Fact]
    public void BackwardExtendedSelection_BoldToggleReflectsActiveCellNotStart() =>
        StaTestRunner.Run(() =>
    {
        var (window, workbook, sheet) = CreateAdoptedWindow();
        try
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var c3 = new CellAddress(sheet.Id, 3, 3);

            var boldStyleId = workbook.RegisterStyle(new CellStyle { Bold = true });
            sheet.SetCell(c3, new Cell { Value = new TextValue("active"), StyleId = boldStyleId });
            sheet.SetCell(a1, new Cell { Value = new TextValue("start") }); // default (not bold)

            // Click C3, then Shift+click A1: active cell stays C3, SelectedRange normalizes to A1:C3.
            SelectBackwardExtendedRangeAndRefreshToolbar(window, anchor: c3, extendTo: a1);

            window.SheetGrid.SelectedRange!.Value.Start.Should().Be(a1,
                "SelectedRange normalizes its Start to the top-left corner");
            window.SheetGrid.ActiveCell.Should().Be(c3, "the true active cell stays at C3");

            GetRibbonChecked(window, "Bold").Should().BeTrue(
                "the Bold ribbon toggle must reflect the ACTIVE cell (C3, bold), not SelectedRange.Start (A1, not bold)");
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });

    /// <summary>No-regression sibling: a forward (top-left-anchored) selection -- where ActiveCell
    /// and SelectedRange.Start coincide -- must keep working exactly as before.</summary>
    [Fact]
    public void ForwardSelection_BoldToggleReflectsStartAsBefore() =>
        StaTestRunner.Run(() =>
    {
        var (window, workbook, sheet) = CreateAdoptedWindow();
        try
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var c3 = new CellAddress(sheet.Id, 3, 3);

            var boldStyleId = workbook.RegisterStyle(new CellStyle { Bold = true });
            sheet.SetCell(a1, new Cell { Value = new TextValue("active"), StyleId = boldStyleId });
            sheet.SetCell(c3, new Cell { Value = new TextValue("end") }); // default (not bold)

            // Click A1, then Shift+click C3: active cell stays A1, which is also SelectedRange.Start.
            SelectBackwardExtendedRangeAndRefreshToolbar(window, anchor: a1, extendTo: c3);

            window.SheetGrid.SelectedRange!.Value.Start.Should().Be(a1);
            window.SheetGrid.ActiveCell.Should().Be(a1);

            GetRibbonChecked(window, "Bold").Should().BeTrue(
                "the active cell A1 is bold and coincides with SelectedRange.Start here");
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });
}
