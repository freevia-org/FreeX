using System.Reflection;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R124 (round 124 review wave): deleting a selected picture/shape/text box/chart (Delete key, R121's
/// DeleteDrawingObjectCommand) explicitly clears SheetGrid.SelectedObjectId/-Kind
/// (MainWindow.Drawing.cs's TryDeleteSelectedDrawingObject). Undoing that delete correctly restores
/// the object to the model (DeleteDrawingObjectCommand.Revert), but before this fix
/// RestoreSelectionAfterUndoRedo (MainWindow.CommandExecution.cs) only ever landed a plain
/// cell-range selection over the object's anchor -- it never re-populated
/// SheetGrid.SelectedObjectId/-Kind, so the restored object rendered with no selection handles and no
/// active Format contextual ribbon tab, unlike real Excel which re-selects the object itself
/// immediately after Ctrl+Z.
/// <para>
/// These tests drive the REAL WPF entry points: ExecuteClearSelection (Delete key) and ExecuteUndo /
/// ExecuteRedo (Ctrl+Z / Ctrl+Y), all via reflection exactly as R121_DeleteDrawingObjectTests and
/// MainWindowOutlineCommandLifecycleTests already do -- never constructing
/// DeleteDrawingObjectCommand or its outcome by hand.
/// </para>
/// </summary>
public sealed class R124_UndoDeleteDrawingObjectSelectionTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void Undo_AfterDeletingSelectedPicture_ReselectsRestoredPicture()
    {
        StaTestRunner.Run(() =>
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance);

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet = workbook.GetSheetAt(0);
                var anchorCell = new CellAddress(sheet.Id, 1, 1);
                var picture = new PictureModel
                {
                    Anchor = anchorCell,
                    Kind = PictureKind.Image,
                    ImageBytes = [1, 2, 3],
                    ContentType = "image/png",
                    Name = "Picture 1"
                };
                var pictureId = picture.Id;
                sheet.Pictures.Add(picture);

                var grid = (GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(anchorCell, anchorCell);
                grid.SelectedObjectId = pictureId;
                grid.SelectedObjectKind = ObjectKind.Picture;

                InvokeExecuteClearSelection(window);
                PumpDispatcher();

                sheet.Pictures.Should().BeEmpty("the delete must have removed the picture");
                grid.SelectedObjectKind.Should().Be(ObjectKind.None, "delete clears the object selection");

                InvokeExecuteUndo(window);
                PumpDispatcher();

                sheet.Pictures.Should().ContainSingle(p => p.Id == pictureId,
                    "undo must restore the deleted picture to the model");
                grid.SelectedObjectId.Should().Be(pictureId,
                    "Excel re-selects the object itself after undoing its deletion, not just its anchor cell");
                grid.SelectedObjectKind.Should().Be(ObjectKind.Picture,
                    "the restored object's kind must drive the Picture Format contextual ribbon tab");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void Undo_AfterDeletingSelectedChart_ReselectsRestoredChart()
    {
        StaTestRunner.Run(() =>
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance);

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet = workbook.GetSheetAt(0);
                var anchorCell = new CellAddress(sheet.Id, 1, 1);
                var chart = new ChartModel
                {
                    Type = ChartType.Column,
                    DataRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2)),
                    Title = "Sales"
                };
                var chartId = chart.Id;
                sheet.Charts.Add(chart);

                var grid = (GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(anchorCell, anchorCell);
                grid.SelectedObjectId = chartId;
                grid.SelectedObjectKind = ObjectKind.Chart;

                InvokeExecuteClearSelection(window);
                PumpDispatcher();

                sheet.Charts.Should().BeEmpty("the delete must have removed the chart");

                InvokeExecuteUndo(window);
                PumpDispatcher();

                sheet.Charts.Should().ContainSingle(c => c.Id == chartId,
                    "undo must restore the deleted chart to the model");
                grid.SelectedObjectId.Should().Be(chartId,
                    "Excel re-selects the object itself after undoing its deletion");
                grid.SelectedObjectKind.Should().Be(ObjectKind.Chart);
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    // No-regression sibling: Ctrl+Z then Ctrl+Y (redo the delete) must not leave the now-stale
    // SelectedObjectId/-Kind pointing at an object that Redo just removed again -- a fix that only
    // handled Undo's "Exists: true" direction and forgot Redo's "Exists: false" mirror would otherwise
    // leave the ribbon's Picture Format tab active (and resize handles rendered) for an object that no
    // longer exists in the model, which is worse than the pre-fix baseline where DrawingObjectSelection
    // was simply never touched by either Undo or Redo.
    [Fact]
    public void Redo_AfterUndoingDelete_ClearsSelectionOfNowReDeletedPicture()
    {
        StaTestRunner.Run(() =>
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance);

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet = workbook.GetSheetAt(0);
                var anchorCell = new CellAddress(sheet.Id, 1, 1);
                var picture = new PictureModel
                {
                    Anchor = anchorCell,
                    Kind = PictureKind.Image,
                    ImageBytes = [1, 2, 3],
                    ContentType = "image/png",
                    Name = "Picture 1"
                };
                var pictureId = picture.Id;
                sheet.Pictures.Add(picture);

                var grid = (GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(anchorCell, anchorCell);
                grid.SelectedObjectId = pictureId;
                grid.SelectedObjectKind = ObjectKind.Picture;

                InvokeExecuteClearSelection(window);
                PumpDispatcher();
                InvokeExecuteUndo(window);
                PumpDispatcher();

                grid.SelectedObjectId.Should().Be(pictureId, "sanity: undo re-selected the restored picture");

                InvokeExecuteRedo(window);
                PumpDispatcher();

                sheet.Pictures.Should().BeEmpty("redo must re-apply the delete");
                grid.SelectedObjectKind.Should().Be(ObjectKind.None,
                    "redo re-deletes the picture, so the stale object selection must be cleared, not left pointing at a removed object");
                grid.SelectedObjectId.Should().Be(Guid.Empty);
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    private static void InvokeExecuteClearSelection(MainWindow window)
    {
        var method = typeof(MainWindow).GetMethod("ExecuteClearSelection", PrivateInstance)
            ?? throw new MissingMethodException(nameof(MainWindow), "ExecuteClearSelection");
        method.Invoke(window, []);
    }

    private static void InvokeExecuteUndo(MainWindow window)
    {
        var method = typeof(MainWindow).GetMethod("ExecuteUndo", PrivateInstance)
            ?? throw new MissingMethodException(nameof(MainWindow), "ExecuteUndo");
        method.Invoke(window, []);
    }

    private static void InvokeExecuteRedo(MainWindow window)
    {
        var method = typeof(MainWindow).GetMethod("ExecuteRedo", PrivateInstance)
            ?? throw new MissingMethodException(nameof(MainWindow), "ExecuteRedo");
        method.Invoke(window, []);
    }
}
