using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FluentAssertions;
using FreeX.App.Presentation.FormulaBar;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

public sealed class R90_CrossWorkbookFormulaPointModeWpfTests
{
    [Fact]
    public void TwoWorkbookWindows_RouteReplaceAppendF4AndCommitToFormulaOwner()
    {
        StaTestRunner.Run(() =>
        {
            var ownerWorkbook = NewWorkbook("Owner.xlsx", "Owner");
            var sourceWorkbook = NewWorkbook("Source.xlsx", "Input Data");
            var registry = new WorkbookWindowRegistry();
            var owner = CreateWindow(ownerWorkbook, registry);
            var source = CreateWindow(sourceWorkbook, registry);
            var formulaCell = new CellAddress(ownerWorkbook.GetSheetAt(0).Id, 8, 7);
            var firstRange = Range(sourceWorkbook.GetSheetAt(0).Id, 2, 2, 2, 2);
            var secondRange = Range(sourceWorkbook.GetSheetAt(0).Id, 4, 3, 4, 3);

            try
            {
                Show(owner);
                Show(source);
                owner.AdoptWorkbookForParityCapture(ownerWorkbook);
                source.AdoptWorkbookForParityCapture(sourceWorkbook);
                PumpDispatcher();
                owner.BeginFormulaPointModeEditForTest(formulaCell, "=SUM(");

                source.RouteFormulaPointSelectionForTest(firstRange).Should().BeTrue();
                owner.FormulaBoxTextForTest.Should().Be("=SUM('[Source.xlsx]Input Data'!B2");
                ((GridView)source.FindName("SheetGrid")).SelectedRange.Should().Be(firstRange);

                source.RouteFormulaPointSelectionForTest(secondRange, append: true).Should().BeTrue();
                owner.FormulaBoxTextForTest.Should().Be(
                    "=SUM('[Source.xlsx]Input Data'!B2,'[Source.xlsx]Input Data'!C4");

                RaiseFormulaKey(owner, Key.F4);
                owner.FormulaBoxTextForTest.Should().Contain("'[Source.xlsx]Input Data'!$C$4");
                owner.FormulaBoxTextForTest += ")";
                RaiseFormulaKey(source, Key.Enter);
                ownerWorkbook.GetSheetAt(0).GetCell(formulaCell)!.FormulaText.Should().Contain(
                    "'[Source.xlsx]Input Data'!");
                owner.HasActiveFormulaPointMode.Should().BeFalse();
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(source);
                MainWindowTestCleanup.CloseWithoutSavePrompt(owner);
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void TwoWorkbookWindows_RouteEscapeToOwnerAndRestoreOriginalCell()
    {
        StaTestRunner.Run(() =>
        {
            var ownerWorkbook = NewWorkbook("Owner.xlsx", "Owner");
            var sourceWorkbook = NewWorkbook("Source.xlsx", "Input Data");
            var registry = new WorkbookWindowRegistry();
            var owner = CreateWindow(ownerWorkbook, registry);
            var source = CreateWindow(sourceWorkbook, registry);
            var formulaCell = new CellAddress(ownerWorkbook.GetSheetAt(0).Id, 8, 7);
            var sourceRange = Range(sourceWorkbook.GetSheetAt(0).Id, 3, 3, 4, 4);

            try
            {
                Show(owner);
                Show(source);
                owner.AdoptWorkbookForParityCapture(ownerWorkbook);
                source.AdoptWorkbookForParityCapture(sourceWorkbook);
                PumpDispatcher();
                owner.BeginFormulaPointModeEditForTest(formulaCell, "=SUM(");
                source.RouteFormulaPointSelectionForTest(sourceRange).Should().BeTrue();
                owner.FormulaBoxTextForTest.Should().Contain("[Source.xlsx]");

                RaiseFormulaKey(source, Key.Escape);

                owner.HasActiveFormulaPointMode.Should().BeFalse();
                ownerWorkbook.GetSheetAt(0).GetCell(formulaCell)?.FormulaText.Should().BeNull();
                owner.FormulaBoxTextForTest.Should().BeEmpty();
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(source);
                MainWindowTestCleanup.CloseWithoutSavePrompt(owner);
                PumpDispatcher();
            }
        });
    }

    private static Workbook NewWorkbook(string name, string sheetName)
    {
        var workbook = new Workbook(name);
        workbook.AddSheet(sheetName);
        return workbook;
    }

    private static MainWindow CreateWindow(Workbook workbook, WorkbookWindowRegistry registry)
    {
        var workbookRef = new WorkbookRef { Current = workbook };
        return new MainWindow(
            NullLogger<MainWindow>.Instance,
            new ViewportService(),
            new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
            new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
            [],
            workbookRef,
            workbook,
            NullUserMessageService.Instance,
            windowRegistry: registry);
    }

    private static void Show(MainWindow window)
    {
        window.Show();
        window.Activate();
        PumpDispatcher();
    }

    private static void RaiseFormulaKey(MainWindow window, Key key)
    {
        var source = PresentationSource.FromVisual(window);
        source.Should().NotBeNull();
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source!, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
        };
        window.RaiseFormulaBoxKeyDownForTest(args);
        PumpDispatcher();
    }

    private static GridRange Range(SheetId sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet, startRow, startCol), new CellAddress(sheet, endRow, endCol));
}
