using System.Reflection;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

// Round-10 regression coverage for the WPF Scenario Manager save path
// (src/FreeX.App.Host/MainWindow.ScenarioCommands.cs) and its shared dialog planner
// (src/FreeX.App.Presentation/ScenarioManager/ScenarioManagerDialogPlanner.cs).
//
// P14: saving a scenario whose changing-cells text is sheet-qualified (e.g. "Sheet2!B1:B3")
// must capture VALUES from that cell's own sheet, not from whatever sheet happens to be active.
//
// P15: saving/editing a scenario whose changing cells are a non-contiguous, comma-separated
// set (e.g. "B1,B3") must preserve exactly those cells - it must not silently absorb the cells
// in between (e.g. B2) by collapsing to a bounding rectangle.
public sealed class FreeXReview10ScenarioTests
{
    [Fact]
    public void SaveScenarioFromDialog_SheetQualifiedChangingCells_CapturesValuesFromTheirOwnSheetNotTheActiveSheet()
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
                new RecordingUserMessageService());

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet1 = workbook.GetSheetAt(0);
                var sheet2 = workbook.AddSheet("Sheet2");

                // Sheet1's B1:B3 holds different (blank/zero) data than Sheet2's B1:B3 -
                // this is what makes the wrong-sheet bug observable.
                sheet2.SetCell(new CellAddress(sheet2.Id, 1, 2), new NumberValue(10));
                sheet2.SetCell(new CellAddress(sheet2.Id, 2, 2), new NumberValue(20));
                sheet2.SetCell(new CellAddress(sheet2.Id, 3, 2), new NumberValue(30));
                sheet1.SetCell(new CellAddress(sheet1.Id, 1, 2), new NumberValue(999));

                // Active sheet stays Sheet1 (index 0) while the changing cells reference Sheet2.
                InvokeSaveScenarioFromDialog(
                    window,
                    scenarioName: "Sheet2 Scenario",
                    changingCellsText: "Sheet2!B1:B3",
                    comment: null,
                    hidden: false,
                    locked: false,
                    replaceScenarioName: null);

                var scenario = workbook.Scenarios.Single(s => s.Name == "Sheet2 Scenario");
                scenario.ChangingCells.Should().HaveCount(3);
                scenario.ChangingCells.Should().OnlyContain(cell => cell.Address.Sheet == sheet2.Id);
                scenario.ChangingCells
                    .OrderBy(cell => cell.Address.Row)
                    .Select(cell => cell.Value)
                    .Should()
                    .Equal(new NumberValue(10), new NumberValue(20), new NumberValue(30));
            }
            finally
            {
                window.SuppressNextClosePrompt();
                window.Close();
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void SaveScenarioFromDialog_NonContiguousChangingCells_DoesNotAbsorbCellsInBetween()
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
                new RecordingUserMessageService());

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet = workbook.GetSheetAt(0);
                var b1 = new CellAddress(sheet.Id, 1, 2);
                var b2 = new CellAddress(sheet.Id, 2, 2);
                var b3 = new CellAddress(sheet.Id, 3, 2);
                sheet.SetCell(b1, new NumberValue(1));
                sheet.SetCell(b2, new NumberValue(999)); // must NOT end up in the scenario
                sheet.SetCell(b3, new NumberValue(3));

                InvokeSaveScenarioFromDialog(
                    window,
                    scenarioName: "Gap Scenario",
                    changingCellsText: "B1,B3",
                    comment: null,
                    hidden: false,
                    locked: false,
                    replaceScenarioName: null);

                var scenario = workbook.Scenarios.Single(s => s.Name == "Gap Scenario");
                scenario.ChangingCells.Select(cell => cell.Address).Should().BeEquivalentTo([b1, b3]);
                scenario.ChangingCells.Should().NotContain(cell => cell.Address == b2);
            }
            finally
            {
                window.SuppressNextClosePrompt();
                window.Close();
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void SaveScenarioFromDialog_EditRoundTripThroughFormatChangingCells_PreservesNonContiguousCellsAcrossSheets()
    {
        // End-to-end regression combining P14 + P15: format an existing cross-sheet,
        // non-contiguous scenario back to text (as the Edit flow does to prefill the dialog),
        // then re-save from that text. The re-saved scenario must match the original exactly -
        // same cells, same per-cell sheet, same values - with nothing absorbed or misattributed.
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
                new RecordingUserMessageService());

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet1 = workbook.GetSheetAt(0);
                var sheet2 = workbook.AddSheet("Sheet2");

                var b1 = new CellAddress(sheet1.Id, 1, 2);
                var b3 = new CellAddress(sheet1.Id, 3, 2);
                var otherB1 = new CellAddress(sheet2.Id, 1, 2);
                sheet1.SetCell(b1, new NumberValue(1));
                sheet1.SetCell(new CellAddress(sheet1.Id, 2, 2), new NumberValue(999)); // must stay excluded
                sheet1.SetCell(b3, new NumberValue(3));
                sheet2.SetCell(otherB1, new NumberValue(7));

                var original = new WorkbookScenario(
                    "Original",
                    [
                        new ScenarioCellValue(b1, new NumberValue(1)),
                        new ScenarioCellValue(b3, new NumberValue(3)),
                        new ScenarioCellValue(otherB1, new NumberValue(7)),
                    ]);
                workbook.Scenarios.Add(original);

                var formatted = FreeX.App.Presentation.ScenarioManager.ScenarioManagerDialogPlanner
                    .FormatChangingCells(workbook, original);

                InvokeSaveScenarioFromDialog(
                    window,
                    scenarioName: "Original",
                    changingCellsText: formatted,
                    comment: "Edited comment",
                    hidden: false,
                    locked: false,
                    replaceScenarioName: "Original");

                var resaved = workbook.Scenarios.Single(s => s.Name == "Original");
                resaved.ChangingCells.Should().HaveCount(3);
                resaved.ChangingCells.Select(cell => cell.Address).Should().BeEquivalentTo(
                    [b1, b3, otherB1]);
                resaved.ChangingCells.Should().NotContain(
                    cell => cell.Address == new CellAddress(sheet1.Id, 2, 2));
                resaved.ChangingCells.First(cell => cell.Address == b1).Value.Should().Be(new NumberValue(1));
                resaved.ChangingCells.First(cell => cell.Address == b3).Value.Should().Be(new NumberValue(3));
                resaved.ChangingCells.First(cell => cell.Address == otherB1).Value.Should().Be(new NumberValue(7));
            }
            finally
            {
                window.SuppressNextClosePrompt();
                window.Close();
                PumpDispatcher();
            }
        });
    }

    private static void InvokeSaveScenarioFromDialog(
        MainWindow window,
        string? scenarioName,
        string? changingCellsText,
        string? comment,
        bool hidden,
        bool locked,
        string? replaceScenarioName)
    {
        window.SaveScenarioFromDialog(scenarioName, changingCellsText, comment, hidden, locked, replaceScenarioName);
    }

    // r446: delegates to the one fixed implementation -- see R49MainWindowTestHarness.
    private static void PumpDispatcher() => R49MainWindowTestHarness.PumpDispatcher();

    /// <summary>
    /// No-op <see cref="IUserMessageService"/> for tests that construct <see cref="MainWindow"/>
    /// directly and don't want real WPF MessageBox windows popping up.
    /// </summary>
    private sealed class RecordingUserMessageService : IUserMessageService
    {
        public void ShowError(string message, string title = "Error") { }
        public void ShowWarning(string message, string title = "Warning") { }
        public void ShowInfo(string message, string title = "Information") { }
        public bool AskYesNo(string message, string title = "Confirm") => false;
        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon) => UserMessageResult.Ok;
    }
}
