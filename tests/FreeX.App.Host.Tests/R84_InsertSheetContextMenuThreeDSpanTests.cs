using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for finding R84-calc-crosssheet-3d-5-3: Insert Sheet from the sheet-tab
/// context menu ignored which tab was right-clicked and always appended the new sheet at the very
/// end of the workbook (via <c>InsertNewSheet</c> -> always-append <c>AddSheetCommand</c>), so a
/// sheet inserted "inside" an existing 3-D span reference (e.g. =SUM(Sheet1:Sheet3!A1)) never
/// actually landed inside it -- diverging from Excel, which inserts immediately BEFORE the
/// acted-on tab.
/// </summary>
public sealed class R84_InsertSheetContextMenuThreeDSpanTests
{
    [Fact]
    public void SheetCtxInsert_Click_InsertsBeforeClickedTabAndExtendsThreeDSpan()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var workbook = harness.Workbook;

            // Order: Sheet1, Sheet2, Sheet3, Sheet4 -- Sheet4 sums the Sheet1:Sheet3 span.
            var sheet1 = workbook.GetSheetAt(0);
            var sheet2 = workbook.AddSheet("Sheet2");
            var sheet3 = workbook.AddSheet("Sheet3");
            var sheet4 = workbook.AddSheet("Sheet4");

            sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
            sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(2));
            sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(3));
            sheet4.SetFormula(new CellAddress(sheet4.Id, 1, 1), "SUM(Sheet1:Sheet3!A1)");

            harness.RecalculateWorkbook();
            sheet4.GetValue(1, 1).Should().Be(new NumberValue(6));

            // Right-click Sheet2's tab and choose Insert -- Excel inserts the new sheet
            // immediately BEFORE Sheet2, landing it inside the Sheet1:Sheet3 span.
            harness.InvokeSheetTabContextMenuClick("SheetCtxInsert_Click", sheet2.Id);

            var sheetIds = workbook.Sheets.Select(s => s.Id).ToList();
            sheetIds.Should().HaveCount(5);
            var insertedSheet = workbook.Sheets[1];
            insertedSheet.Id.Should().NotBe(sheet1.Id);
            insertedSheet.Id.Should().NotBe(sheet2.Id);
            sheetIds.IndexOf(sheet2.Id).Should().Be(
                2, "the inserted sheet must land immediately before Sheet2, pushing it from index 1 to index 2");

            // A value on the newly inserted sheet (now positioned inside the Sheet1:Sheet3 span)
            // must be picked up by Sheet4's SUM once recalculated -- this only holds if the new
            // sheet actually landed BETWEEN Sheet1 and Sheet3, not appended after Sheet4.
            insertedSheet.SetCell(new CellAddress(insertedSheet.Id, 1, 1), new NumberValue(100));
            harness.RecalculateWorkbook();

            sheet4.GetValue(1, 1).Should().Be(
                new NumberValue(1 + 100 + 2 + 3),
                "the sheet inserted via the tab context menu must fall inside the Sheet1:Sheet3 span, matching Excel's insert-before-acted-on-tab placement");
        });
    }

    [Fact]
    public void AddSheetButton_StillAppendsAtEndAndDoesNotExtendThreeDSpan()
    {
        // No-regression sibling: the tab-strip '+' button (and any other append-only caller of
        // InsertNewSheet with no target tab) must keep its pre-fix append-at-end behavior, matching
        // real Excel's own New Sheet button -- only the context-menu Insert (with an explicit
        // target tab) becomes position-aware.
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var workbook = harness.Workbook;

            var sheet1 = workbook.GetSheetAt(0);
            var sheet2 = workbook.AddSheet("Sheet2");
            var sheet3 = workbook.AddSheet("Sheet3");
            var sheet4 = workbook.AddSheet("Sheet4");

            sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
            sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(2));
            sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(3));
            sheet4.SetFormula(new CellAddress(sheet4.Id, 1, 1), "SUM(Sheet1:Sheet3!A1)");

            harness.RecalculateWorkbook();
            sheet4.GetValue(1, 1).Should().Be(new NumberValue(6));

            harness.InvokeInsertNewSheet(insertBeforeSheetId: null);

            workbook.Sheets.Should().HaveCount(5);
            var appendedSheet = workbook.Sheets[^1];
            appendedSheet.Id.Should().NotBe(sheet4.Id);

            appendedSheet.SetCell(new CellAddress(appendedSheet.Id, 1, 1), new NumberValue(999));
            harness.RecalculateWorkbook();

            sheet4.GetValue(1, 1).Should().Be(
                new NumberValue(6),
                "a sheet appended at the end (no target tab) must stay outside the Sheet1:Sheet3 span, unchanged from before this fix");
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        public MainWindow Window { get; }
        public Workbook Workbook { get; }

        public MainWindowHarness()
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            Window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance);

            Window.Show();
            PumpDispatcher();

            // MainWindow_Loaded (fired by Show() above) replaces the constructor-supplied workbook
            // with a fresh one via CreateNewWorkbook() -- capture the *live* workbook afterward so
            // the test operates on the same Workbook instance MainWindow's handlers use.
            Workbook = workbookRef.Current;
        }

        public void RecalculateWorkbook() => InvokeVoid("RecalculateWorkbook");

        public void InvokeVoid(string methodName)
        {
            var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic, [])
                ?? throw new MissingMethodException(nameof(MainWindow), methodName);
            method.Invoke(Window, []);
        }

        public void InvokeInsertNewSheet(SheetId? insertBeforeSheetId)
        {
            Window.InsertNewSheet(insertBeforeSheetId);
        }

        public void InvokeSheetTabContextMenuClick(string methodName, SheetId clickedSheetId)
        {
            var tab = new SheetTabViewModel(clickedSheetId, "irrelevant", null);
            var placementTarget = new Border { DataContext = tab };
            var menuItem = new MenuItem();
            var contextMenu = new ContextMenu { PlacementTarget = placementTarget };
            contextMenu.Items.Add(menuItem);

            var method = typeof(MainWindow).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic,
                [typeof(object), typeof(RoutedEventArgs)]);
            method.Should().NotBeNull($"{methodName} should exist as a private click handler on MainWindow");
            method!.Invoke(Window, [menuItem, new RoutedEventArgs()]);
        }

        public void Dispose()
        {
            foreach (Window ownedWindow in Window.OwnedWindows.Cast<Window>().ToList())
                ownedWindow.Close();
            MainWindowTestCleanup.CloseWithoutSavePrompt(Window);
            PumpDispatcher();
        }
    }

    // r446: delegates to the one fixed implementation -- see DispatcherTestPump.
    private static void PumpDispatcher() => DispatcherTestPump.PumpDispatcher();
}
