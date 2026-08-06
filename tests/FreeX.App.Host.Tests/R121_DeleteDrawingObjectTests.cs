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
/// R121 (round 111 backlog): FreeX had no way to delete a picture/text box/shape/chart -- the Delete
/// key always routed to ExecuteClearSelection's ClearContentsCommand over the cell range, never
/// touching a selected drawing object. These tests drive the REAL WPF entry point
/// (ExecuteClearSelection, the same private method the Delete-key keyboard shortcut dispatches to --
/// see MainWindow.KeyboardCommands.cs's KeyboardCommandShortcut.ClearSelection registration) via
/// reflection, mirroring MainWindowFormulaBarSyncTests.Harness.cs's existing convention, rather than
/// constructing DeleteDrawingObjectCommand by hand.
/// </summary>
public sealed class R121_DeleteDrawingObjectTests
{
    [Fact]
    public void DeleteKey_WithPictureSelected_RemovesObjectNotCellContents()
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
                sheet.SetCell(anchorCell, new NumberValue(99));
                var picture = new PictureModel
                {
                    Anchor = anchorCell,
                    Kind = PictureKind.Image,
                    ImageBytes = [1, 2, 3],
                    ContentType = "image/png",
                    Name = "Picture 1"
                };
                sheet.Pictures.Add(picture);

                var grid = (GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(anchorCell, anchorCell);
                grid.SelectedObjectId = picture.Id;
                grid.SelectedObjectKind = ObjectKind.Picture;

                InvokeExecuteClearSelection(window);
                PumpDispatcher();

                sheet.Pictures.Should().BeEmpty("Delete on a selected picture must remove it");
                sheet.GetCell(anchorCell)!.Value.Should().Be(new NumberValue(99),
                    "Delete on a selected object must not also clear the cell underneath it");
                grid.SelectedObjectKind.Should().Be(ObjectKind.None, "selection must clear after delete");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void DeleteKey_WithChartSelected_RemovesChart()
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
                sheet.Charts.Add(chart);

                var grid = (GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(anchorCell, anchorCell);
                grid.SelectedObjectId = chart.Id;
                grid.SelectedObjectKind = ObjectKind.Chart;

                InvokeExecuteClearSelection(window);
                PumpDispatcher();

                sheet.Charts.Should().BeEmpty("Delete on a selected chart must remove it");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void DeleteKey_WithNoObjectSelected_StillClearsCellContents()
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
                var cell = new CellAddress(sheet.Id, 2, 2);
                sheet.SetCell(cell, new NumberValue(7));

                var grid = (GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(cell, cell);
                grid.SelectedObjectId = Guid.Empty;
                grid.SelectedObjectKind = ObjectKind.None;

                InvokeExecuteClearSelection(window);
                PumpDispatcher();

                sheet.GetCell(cell)?.Value.Should().Be(BlankValue.Instance,
                    "with no drawing object selected, Delete must fall through to ordinary Clear Contents");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void DeleteKey_OnLockedPictureUnderEditObjectsProtection_IsRejected()
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
                    Locked = true
                };
                sheet.Pictures.Add(picture);
                sheet.IsProtected = true;

                var grid = (GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(anchorCell, anchorCell);
                grid.SelectedObjectId = picture.Id;
                grid.SelectedObjectKind = ObjectKind.Picture;

                InvokeExecuteClearSelection(window);
                PumpDispatcher();

                sheet.Pictures.Should().ContainSingle(
                    "a locked picture must not be deleted while the sheet blocks Edit Objects");
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
        var method = typeof(MainWindow).GetMethod(
            "ExecuteClearSelection",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "ExecuteClearSelection");
        method.Invoke(window, []);
    }
}
