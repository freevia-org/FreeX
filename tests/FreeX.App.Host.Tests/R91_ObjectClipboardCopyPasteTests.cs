using System.Windows;
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
/// R91-io-clipboard-image-formats-5-1: before this fix, Ctrl+C/Ctrl+V on a selected chart or shape
/// never duplicated the object -- ExecuteCopy only ever read SheetGrid.SelectedRange (whatever
/// single cell happened to sit under the object's anchor), never SheetGrid.SelectedObjectKind/Id,
/// so it silently copied that underlying cell instead and Ctrl+V pasted ordinary cell content.
/// These tests drive the REAL product entry points (CopyBtn_Click/PasteBtn_Click on a live
/// MainWindow), matching the existing MainWindowClipboardCutMoveTests/MainWindowClipboardGroupedSheetTests
/// convention, rather than constructing DuplicateDrawingObjectCommand by hand.
/// </summary>
public sealed class R91_ObjectClipboardCopyPasteTests
{
    [Fact]
    public void CopyThenPaste_WithChartSelected_DuplicatesTheChartNotTheUnderlyingCell()
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
                // The cell under the chart's anchor carries its own value/formula -- proving the
                // duplicate is a CHART, not this cell, is the whole point of the regression.
                sheet.SetCell(anchorCell, new NumberValue(99));
                var chart = new ChartModel
                {
                    Type = ChartType.Column,
                    DataRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2)),
                    Title = "Sales",
                    Left = 10,
                    Top = 10
                };
                sheet.Charts.Add(chart);

                var grid = (GridView)window.FindName("SheetGrid");
                // Mirrors SelectInsertedChart: SelectedRange stays a plain single-cell range under
                // the object while SelectedObjectKind/Id identify the actual selected object.
                grid.SelectedRange = new GridRange(anchorCell, anchorCell);
                grid.SelectedObjectId = chart.Id;
                grid.SelectedObjectKind = ObjectKind.Chart;

                InvokeClickHandler(window, "CopyBtn_Click");
                PumpDispatcher();
                InvokeClickHandler(window, "PasteBtn_Click");
                PumpDispatcher();

                sheet.Charts.Should().HaveCount(2, "Ctrl+V on a copied chart must duplicate it");
                sheet.Charts.Should().Contain(c => c.Id == chart.Id, "the original chart must be untouched by a plain copy");
                var duplicate = sheet.Charts.Single(c => c.Id != chart.Id);
                duplicate.Title.Should().Be("Sales");
                duplicate.Type.Should().Be(ChartType.Column);

                // The underlying cell must be left completely alone -- the pre-fix bug copied THIS
                // cell instead of the chart, which this asserts never happened.
                sheet.GetCell(anchorCell)!.Value.Should().Be(new NumberValue(99));
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void CopyThenPaste_WithNoObjectSelected_StillCopiesTheCellNormally()
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
                var a1 = new CellAddress(sheet.Id, 1, 1);
                var d1 = new CellAddress(sheet.Id, 1, 4);
                sheet.SetCell(a1, new NumberValue(42));

                var grid = (GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(a1, a1);
                // No object selected -- ObjectKind defaults to None, matching a plain cell click.
                grid.SelectedObjectKind.Should().Be(ObjectKind.None);

                InvokeClickHandler(window, "CopyBtn_Click");
                PumpDispatcher();

                grid.SelectedRange = new GridRange(d1, d1);
                InvokeClickHandler(window, "PasteBtn_Click");
                PumpDispatcher();

                sheet.GetCell(d1)!.Value.Should().Be(new NumberValue(42));
                sheet.Charts.Should().BeEmpty();
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void CutThenPaste_WithChartSelected_MovesTheChartAfterPaste()
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
                var anchor = new CellAddress(sheet.Id, 2, 2);
                var chart = new ChartModel
                {
                    Type = ChartType.Column,
                    DataRange = new GridRange(anchor, new CellAddress(sheet.Id, 5, 3)),
                    Title = "Move me"
                };
                sheet.Charts.Add(chart);

                var grid = (GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(anchor, anchor);
                grid.SelectedObjectId = chart.Id;
                grid.SelectedObjectKind = ObjectKind.Chart;

                InvokeClickHandler(window, "CutBtn_Click");
                PumpDispatcher();
                sheet.Charts.Should().ContainSingle("Cut only arms the object move");
                sheet.Charts[0].Id.Should().Be(chart.Id);

                InvokeClickHandler(window, "PasteBtn_Click");
                PumpDispatcher();

                sheet.Charts.Should().ContainSingle();
                sheet.Charts[0].Id.Should().NotBe(chart.Id);
                sheet.Charts[0].Title.Should().Be("Move me");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    private static void InvokeClickHandler(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            [typeof(object), typeof(RoutedEventArgs)]);
        method.Should().NotBeNull($"{methodName} should exist as a private click handler on MainWindow");
        method!.Invoke(window, [window, new RoutedEventArgs()]);
    }
}
