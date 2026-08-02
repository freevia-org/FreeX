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
/// Regression coverage for R90-app-camera-picture-link-5-1: the WPF host (FreeX.App.Host, the
/// primary shipping shell) never refreshed a linked/Camera picture's rendered snapshot after the
/// initial Paste Special &gt; Linked Picture -- it only ever updated via the unrelated structural
/// row/column-shift path (RowColumnShiftHelpers.RefreshLinkedPictureSnapshot). A plain value edit to
/// a cell inside the picture's LinkedSourceRange left the picture showing stale, paste-time content
/// forever. Drives the same command choke point every real cell-edit command reaches
/// (MainWindow.TryExecuteCommand, via reflection since it is a private instance method) rather than
/// mutating the PictureModel directly, so the test exercises the actual fixed code path.
/// </summary>
public sealed class R90_LinkedPictureRefreshOnEditTests
{
    /// <summary>Placeholder registered ahead of time so the real window adopts our seeded workbook
    /// instead of MainWindow_Loaded replacing it with a fresh one.</summary>
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

    /// <summary>Drives the exact same private choke point every real cell-edit command in
    /// MainWindow.CommandExecution.cs ultimately reaches.</summary>
    private static bool ExecuteCommandThroughRealChokePoint(MainWindow window, IWorkbookCommand command)
    {
        var method = typeof(MainWindow).GetMethod(
            "TryExecuteCommand",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(IWorkbookCommand), typeof(string)],
            modifiers: null);
        method.Should().NotBeNull();
        return (bool)method!.Invoke(window, [command, "Test Command"])!;
    }

    [Fact]
    public void EditingACellInsideTheLinkedRange_RefreshesTheLinkedPicturesCachedSnapshot() =>
        StaTestRunner.Run(() =>
    {
        var (window, workbook, sheet) = CreateAdoptedWindow();
        try
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b1 = new CellAddress(sheet.Id, 1, 2);
            var sourceRange = new GridRange(a1, b1);
            var destination = new CellAddress(sheet.Id, 5, 4);

            // Seed the source range (the real cell-edit choke point).
            ExecuteCommandThroughRealChokePoint(
                window,
                new EditCellsCommand(sheet.Id, [
                    (a1, Cell.FromValue(new TextValue("Q1"))),
                    (b1, Cell.FromValue(new NumberValue(10)))
                ])).Should().BeTrue();

            // Paste Special > Linked Picture: the same PasteRangeAsPictureCommand
            // MainWindow.ClipboardCommands.cs constructs for that feature, run through the same
            // choke point.
            ExecuteCommandThroughRealChokePoint(
                window,
                new PasteRangeAsPictureCommand(
                    sheet.Id,
                    sourceRange,
                    [(a1, "Q1"), (b1, "10")],
                    destination,
                    isLinkedToSourceRange: true,
                    sourceSheetName: sheet.Name)).Should().BeTrue();

            var picture = sheet.Pictures.Should().ContainSingle().Subject;
            picture.IsLinkedToSourceRange.Should().BeTrue();
            picture.Cells.Should().Contain(c => c.RowOffset == 0 && c.ColumnOffset == 0 && c.Text == "Q1");

            // The failure scenario: type a new value into A1 and press Enter -- an ordinary,
            // in-range value edit that does NOT move the source range's coordinates (so the
            // structural row/column-shift refresh path never fires).
            ExecuteCommandThroughRealChokePoint(
                window,
                new EditCellsCommand(sheet.Id, a1, new TextValue("CHANGED"))).Should().BeTrue();

            picture.Cells.Should().Contain(
                c => c.RowOffset == 0 && c.ColumnOffset == 0 && c.Text == "CHANGED",
                "the linked picture must refresh from the live sheet on an ordinary in-range edit, " +
                "not just on a structural row/column shift");
            picture.Cells.Should().NotContain(c => c.Text == "Q1", "the stale paste-time snapshot must be gone");
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });

    /// <summary>No-regression sibling: an edit OUTSIDE the linked picture's source range must not
    /// touch its cached snapshot at all.</summary>
    [Fact]
    public void EditingACellOutsideTheLinkedRange_LeavesTheLinkedPicturesSnapshotUntouched() =>
        StaTestRunner.Run(() =>
    {
        var (window, workbook, sheet) = CreateAdoptedWindow();
        try
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b1 = new CellAddress(sheet.Id, 1, 2);
            var farCell = new CellAddress(sheet.Id, 20, 20);
            var sourceRange = new GridRange(a1, b1);
            var destination = new CellAddress(sheet.Id, 5, 4);

            ExecuteCommandThroughRealChokePoint(
                window,
                new EditCellsCommand(sheet.Id, [
                    (a1, Cell.FromValue(new TextValue("Q1"))),
                    (b1, Cell.FromValue(new NumberValue(10)))
                ])).Should().BeTrue();

            ExecuteCommandThroughRealChokePoint(
                window,
                new PasteRangeAsPictureCommand(
                    sheet.Id,
                    sourceRange,
                    [(a1, "Q1"), (b1, "10")],
                    destination,
                    isLinkedToSourceRange: true,
                    sourceSheetName: sheet.Name)).Should().BeTrue();

            var picture = sheet.Pictures.Should().ContainSingle().Subject;
            var snapshotBefore = picture.Cells.Select(c => c with { }).ToList();

            ExecuteCommandThroughRealChokePoint(
                window,
                new EditCellsCommand(sheet.Id, farCell, new TextValue("unrelated"))).Should().BeTrue();

            picture.Cells.Should().BeEquivalentTo(snapshotBefore,
                "an edit outside the linked source range must not rebuild the picture's snapshot");
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });
}
