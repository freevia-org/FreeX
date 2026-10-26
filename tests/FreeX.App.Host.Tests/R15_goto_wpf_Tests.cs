using System.Reflection;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R15-name-manager-goto-2: ResolveGoToSpecialSearchRange expands a single-cell selection to the
/// sheet's used range so *content* kinds (Constants/Blanks/Formulas/etc.) search the whole sheet,
/// but CurrentRegion/Precedents/Dependents must still be traced from the user's TRUE active cell
/// (not the used range's top-left corner), or:
///   - CurrentRegion falsely reports "No cells found" whenever that corner happens to be blank.
///   - Precedents/Dependents pick up unrelated cells from across the whole used range instead of
///     only the cell the user actually selected.
/// </summary>
public sealed class R15_goto_wpf_Tests
{
    [Fact]
    public void GoToSpecial_CurrentRegion_FromNonCornerActiveCell_SelectsRegionAroundActiveCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            // Used range is A2:D6 (forced by the D2/A6 corner values below); A2 itself is left
            // blank. C4 is an isolated non-blank cell with no adjacent content, so its current
            // region is exactly C4:C4 -- but only if CurrentRegion is traced from C4, not from the
            // (blank) used-range corner A2.
            harness.SetCellNumber(2, 4, 100);  // D2 - used-range corner
            harness.SetCellNumber(6, 1, 200);  // A6 - used-range corner
            harness.SetCellFormula(4, 3, "=1+1"); // C4 - the cell the user actually selects

            harness.SelectActiveCell(4, 3);

            harness.InvokeGoToSpecial(GoToSpecialKind.CurrentRegion);

            harness.LastNoCellsFoundMessageShown.Should().BeFalse();
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 4, 3),
                new CellAddress(harness.CurrentSheetId, 4, 3)));
        });
    }

    [Fact]
    public void GoToSpecial_Precedents_FromNonCornerActiveCell_TracesOnlyActiveCellsFormula()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellNumber(2, 4, 100); // D2 - used-range corner + precedent of C4
            harness.SetCellNumber(6, 1, 200); // A6 - used-range corner + precedent of C4
            harness.SetCellFormula(4, 3, "=A6+D2"); // C4 - the selected cell
            // Decoy formula elsewhere in the used range: if Precedents wrongly traces the whole
            // used range instead of just the active cell, its precedent (A2) leaks into the result.
            harness.SetCellFormula(6, 2, "=A2+1"); // B6

            harness.SelectActiveCell(4, 3);

            harness.InvokeGoToSpecial(GoToSpecialKind.Precedents);

            var expected = new[]
            {
                new CellAddress(harness.CurrentSheetId, 6, 1), // A6
                new CellAddress(harness.CurrentSheetId, 2, 4)  // D2
            };
            harness.SelectedCells.Should().BeEquivalentTo(expected);
        });
    }

    [Fact]
    public void GoToSpecial_Dependents_FromNonCornerActiveCell_TracesOnlyDependentsOfActiveCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellNumber(2, 4, 100); // D2 - used-range corner
            harness.SetCellNumber(6, 1, 200); // A6 - used-range corner
            harness.SetCellFormula(4, 3, "=A6+D2"); // C4 - the selected cell
            harness.SetCellFormula(3, 2, "=C4+1"); // B3 - true dependent of C4
            // Decoy formula elsewhere in the used range: if Dependents wrongly traces the whole
            // used range instead of just the active cell, this shows up too (it references A2,
            // which lies inside the used range but has nothing to do with C4).
            harness.SetCellFormula(6, 2, "=A2+1"); // B6

            harness.SelectActiveCell(4, 3);

            harness.InvokeGoToSpecial(GoToSpecialKind.Dependents);

            harness.SelectedCells.Should().BeEquivalentTo(
            [
                new CellAddress(harness.CurrentSheetId, 3, 2) // B3
            ]);
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly SpyUserMessageService _messageService;
        private readonly MethodInfo _recalculateWorkbook;
        private readonly Workbook _workbook;

        private MainWindowHarness(MainWindow window, SpyUserMessageService messageService, Workbook workbook)
        {
            _window = window;
            _messageService = messageService;
            _workbook = workbook;
            _recalculateWorkbook = typeof(MainWindow)
                .GetMethod("RecalculateWorkbook", BindingFlags.Instance | BindingFlags.NonPublic, [])
                ?? throw new MissingMethodException(nameof(MainWindow), "RecalculateWorkbook");
        }

        public SheetId CurrentSheetId => _workbook.Sheets[0].Id;

        public GridRange? SelectedRange => ((SheetGridView)_window.FindName("SheetGrid")).SelectedRange;

        public IReadOnlyList<CellAddress> SelectedCells
        {
            get
            {
                var grid = (SheetGridView)_window.FindName("SheetGrid");
                if (grid.SelectedRanges is { Count: > 0 } ranges)
                    return ranges.SelectMany(r => r.AllCells()).ToList();

                return grid.SelectedRange is { } range
                    ? range.AllCells().ToList()
                    : [];
            }
        }

        public bool LastNoCellsFoundMessageShown => _messageService.NoCellsFoundMessageShown;

        public void SetCellNumber(uint row, uint col, double value)
        {
            var sheet = _workbook.Sheets[0];
            sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(value));
        }

        public void SetCellFormula(uint row, uint col, string formulaText)
        {
            var sheet = _workbook.Sheets[0];
            sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromFormula(formulaText));
            _recalculateWorkbook.Invoke(_window, []);
        }

        public void SelectActiveCell(uint row, uint col)
        {
            var sheet = _workbook.Sheets[0];
            _window.SetActiveCell(new CellAddress(sheet.Id, row, col));
            PumpDispatcher();
        }

        public void InvokeGoToSpecial(GoToSpecialKind kind)
        {
            _window.SelectGoToSpecialMatches(kind, showEmptyMessage: true);
            PumpDispatcher();
        }

        public static MainWindowHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
            var messageService = new SpyUserMessageService();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                commandBus,
                new RecalcEngine(graph, evaluator),
                [],
                workbookRef,
                workbook,
                messageService)
            {
                WindowState = System.Windows.WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            window.Show();
            window.Activate();
            window.UpdateLayout();
            PumpDispatcher();

            // MainWindow_Loaded replaces the constructor-supplied workbook with a fresh one via
            // CreateNewWorkbook() (there is no window registry here, so the shared-workbook adopt
            // path is not taken) -- capture the *live* workbook afterward so the test operates on
            // the same instance MainWindow's Go To Special handlers actually read from.
            return new MainWindowHarness(window, messageService, workbookRef.Current);
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
            PumpDispatcher();
        }
    }

    private sealed class SpyUserMessageService : IUserMessageService
    {
        public bool NoCellsFoundMessageShown { get; private set; }

        public void ShowError(string message, string title = "Error") { }
        public void ShowWarning(string message, string title = "Warning") { }

        public void ShowInfo(string message, string title = "Information")
        {
            if (message == UiText.Get("GoToSpecial_NoCellsFoundMessage"))
                NoCellsFoundMessageShown = true;
        }

        public bool AskYesNo(string message, string title = "Confirm") => false;

        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon) =>
            UserMessageResult.Ok;
    }

    // r446: delegates to the one fixed implementation -- see DispatcherTestPump.
    private static void PumpDispatcher() => DispatcherTestPump.PumpDispatcher();
}
