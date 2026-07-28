using System.Reflection;
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

// Regression coverage for G21/G22/G35 (WPF host clipboard grouped-sheet parity with the
// FreeX.App.Services.WorkbookSession twin): grouping two or more sheet tabs must mirror
// Paste, cut+paste, and Paste Special > Picture/Linked Picture across every grouped sheet,
// exactly like Excel's grouped-sheet editing and exactly like the Avalonia/session host.
public sealed class MainWindowClipboardGroupedSheetTests
{
    [Fact]
    public void Paste_WithGroupedSheets_MirrorsPasteToAllGroupedSheets()
    {
        StaTestRunner.RunClipboardIsolated(() =>
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
                var sheet1 = workbook.GetSheetAt(0);
                var sheet2 = workbook.AddSheet("Sheet2");
                GroupSheets(window, sheet1.Id, sheet2.Id);

                var a1 = new CellAddress(sheet1.Id, 1, 1);
                var b1 = new CellAddress(sheet1.Id, 1, 2);
                sheet1.SetCell(a1, new NumberValue(42));

                var grid = (GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(a1, a1);

                InvokeClickHandler(window, "CopyBtn_Click");
                PumpDispatcher();

                grid.SelectedRange = new GridRange(b1, b1);
                InvokeClickHandler(window, "PasteBtn_Click");
                PumpDispatcher();

                // Active sheet receives the paste as usual.
                sheet1.GetCell(b1)!.Value.Should().Be(new NumberValue(42));
                // Grouped sibling sheet must mirror the same paste (Excel grouped-sheet parity),
                // matching FreeX.App.Services.WorkbookSession.CreateInternalPasteCommand.
                var sheet2AfterPaste = workbook.GetSheet(sheet2.Id)!;
                sheet2AfterPaste.GetCell(new CellAddress(sheet2.Id, 1, 2))!.Value.Should().Be(new NumberValue(42));
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void CutThenPaste_WithGroupedSheets_FallsBackToGroupedCopyAndClearInsteadOfSingleSheetMove()
    {
        StaTestRunner.RunClipboardIsolated(() =>
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
                var sheet1 = workbook.GetSheetAt(0);
                var sheet2 = workbook.AddSheet("Sheet2");
                GroupSheets(window, sheet1.Id, sheet2.Id);

                var a1 = new CellAddress(sheet1.Id, 1, 1);
                var d1 = new CellAddress(sheet1.Id, 1, 4);
                sheet1.SetCell(a1, new NumberValue(7));

                var grid = (GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(a1, a1);

                InvokeClickHandler(window, "CutBtn_Click");
                PumpDispatcher();

                grid.SelectedRange = new GridRange(d1, d1);
                InvokeClickHandler(window, "PasteBtn_Click");
                PumpDispatcher();

                // Grouped cut+paste cannot be a same-sheet MoveRangeCommand across multiple
                // grouped sheets, so the host must fall back to the grouped copy+clear path
                // (same as FreeX.App.Services.WorkbookSession.TryCreateCutMoveCommand) and
                // mirror the paste to the other grouped sheet.
                sheet1.GetCell(d1)!.Value.Should().Be(new NumberValue(7));
                // ClearContentsCommand blanks the cell's value but (by convention) may leave the
                // Cell object in place to preserve formatting — assert on the VALUE, not presence.
                sheet1.GetValue(a1).Should().Be(BlankValue.Instance);
                var sheet2AfterPaste = workbook.GetSheet(sheet2.Id)!;
                sheet2AfterPaste.GetCell(new CellAddress(sheet2.Id, 1, 4))!.Value.Should().Be(new NumberValue(7));
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void PasteAsPicture_WithGroupedSheets_InsertsPictureOnEveryGroupedSheet()
    {
        StaTestRunner.RunClipboardIsolated(() =>
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
                var sheet1 = workbook.GetSheetAt(0);
                var sheet2 = workbook.AddSheet("Sheet2");
                GroupSheets(window, sheet1.Id, sheet2.Id);

                var a1 = new CellAddress(sheet1.Id, 1, 1);
                var b1 = new CellAddress(sheet1.Id, 1, 2);
                sheet1.SetCell(a1, new NumberValue(9));

                var grid = (GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(a1, a1);

                InvokeClickHandler(window, "CopyBtn_Click");
                PumpDispatcher();

                grid.SelectedRange = new GridRange(b1, b1);
                InvokeClickHandler(window, "PastePictureMenuItem_Click");
                PumpDispatcher();

                sheet1.Pictures.Should().ContainSingle(p => p.Anchor == b1);
                var sheet2AfterPaste = workbook.GetSheet(sheet2.Id)!;
                var sheet2Anchor = new CellAddress(sheet2.Id, 1, 2);
                sheet2AfterPaste.Pictures.Should().ContainSingle(p => p.Anchor == sheet2Anchor);
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    private static void GroupSheets(MainWindow window, params SheetId[] sheetIds)
    {
        var field = typeof(MainWindow).GetField("_groupedSheetIds", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_groupedSheetIds");
        var groupedSheetIds = (HashSet<SheetId>)field.GetValue(window)!;
        groupedSheetIds.Clear();
        foreach (var sheetId in sheetIds)
            groupedSheetIds.Add(sheetId);
    }

    private static void InvokeClickHandler(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(object), typeof(RoutedEventArgs)]);
        method.Should().NotBeNull($"{methodName} should exist as a private click handler on MainWindow");
        method!.Invoke(window, [window, new RoutedEventArgs()]);
    }
}
