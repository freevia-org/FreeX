using System.Reflection;
using System.Windows.Controls;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R119-app-host-except-data-tables-recalc: r118 fixed "AutomaticExceptDataTables never excluded
/// Data Tables" only in the shared <see cref="FreeX.App.Services.WorkbookCellEditService"/> that the
/// Avalonia shell's <c>WorkbookSession</c> uses. The WPF host (<see cref="MainWindow"/>) never routes
/// cell edits or recalculation through that service -- every ordinary edit recalculates through its
/// own private <c>RecalculateIfAutomatic</c> (MainWindow.WorkbookUiState.cs), and F9/Shift+F9/
/// Ctrl+Alt+F9 call the raw <c>RecalcEngine</c> directly (MainWindow.FormulaCommands.cs /
/// MainWindow.WorkbookUiState.cs) -- so the r118 fix never reached this platform at all. These tests
/// go through the real product entry points: the real <see cref="CommandBus"/> for creating the Data
/// Table and switching calculation mode, the real formula-bar commit path (<c>CommitEdit</c>) for an
/// ordinary automatic-mode edit, and the real ribbon button click handlers
/// (<c>CalcNowBtn_Click</c>/<c>CalcSheetBtn_Click</c>/<c>CalcFullBtn_Click</c>) for F9/Shift+F9/
/// Ctrl+Alt+F9 -- not a hand-built model or a direct call to the private recalc helper.
/// </summary>
public sealed class R119_AppHostExceptDataTablesRecalcTests
{
    [Fact]
    public void AutomaticExceptDataTables_CommitEditToUnrelatedPrecedent_LeavesDataTableBodyFrozenUntilF9()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new DataTableRecalcHarness();
            var (multiplier, bodyD2, bodyD3) = harness.CreateOneVariableDataTable();

            harness.Sheet.GetCell(bodyD2)!.Value.Should().Be(new NumberValue(2));
            harness.Sheet.GetCell(bodyD3)!.Value.Should().Be(new NumberValue(4));

            harness.SetCalculationMode(WorkbookCalculationMode.AutomaticExceptDataTables);

            // Commit "5" into the multiplier cell (an ordinary precedent the Data Table's master
            // formula reads -- NOT the master formula cell itself) via the real formula-bar commit
            // path every ordinary WPF-host cell edit uses (CommitEdit -> CommitPreparedEdits ->
            // RecalculateIfAutomatic).
            harness.CommitCellEdit(multiplier, "5");

            harness.Sheet.GetCell(multiplier)!.Value.Should().Be(new NumberValue(5));
            harness.Sheet.GetCell(bodyD2)!.Value.Should().Be(
                new NumberValue(2),
                "a Data Table body cell must not recalculate automatically in AutomaticExceptDataTables mode");
            harness.Sheet.GetCell(bodyD3)!.Value.Should().Be(
                new NumberValue(4),
                "a Data Table body cell must not recalculate automatically in AutomaticExceptDataTables mode");

            // F9 ("Calculate Now") must still force the Data Table to pick up the new precedent value.
            harness.InvokePrivateHandler("CalcNowBtn_Click");

            harness.Sheet.GetCell(bodyD2)!.Value.Should().Be(new NumberValue(5));
            harness.Sheet.GetCell(bodyD3)!.Value.Should().Be(new NumberValue(10));
        });
    }

    // No-regression sibling: the identical edit, in plain Automatic mode, must keep rippling into the
    // Data Table exactly as before -- AutomaticExceptDataTables is the only mode that gets the new
    // carve-out, and this must not regress on the WPF host.
    [Fact]
    public void Automatic_CommitEditToUnrelatedPrecedent_StillRecalculatesDataTableBodyImmediately()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new DataTableRecalcHarness();
            var (multiplier, bodyD2, bodyD3) = harness.CreateOneVariableDataTable();

            harness.Workbook.CalculationMode.Should().Be(WorkbookCalculationMode.Automatic);

            harness.CommitCellEdit(multiplier, "5");

            harness.Sheet.GetCell(bodyD2)!.Value.Should().Be(new NumberValue(5));
            harness.Sheet.GetCell(bodyD3)!.Value.Should().Be(new NumberValue(10));
        });
    }

    [Fact]
    public void ShiftF9CalcSheet_ForcesDataTableFreshEvenWhenFrozenByAutomaticExceptDataTables()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new DataTableRecalcHarness();
            var (multiplier, bodyD2, bodyD3) = harness.CreateOneVariableDataTable();

            harness.SetCalculationMode(WorkbookCalculationMode.AutomaticExceptDataTables);
            harness.CommitCellEdit(multiplier, "5");
            harness.Sheet.GetCell(bodyD2)!.Value.Should().Be(new NumberValue(2), "frozen before Shift+F9");

            harness.InvokePrivateHandler("CalcSheetBtn_Click");

            harness.Sheet.GetCell(bodyD2)!.Value.Should().Be(new NumberValue(5));
            harness.Sheet.GetCell(bodyD3)!.Value.Should().Be(new NumberValue(10));
        });
    }

    [Fact]
    public void CtrlAltF9CalcFull_ForcesDataTableFreshEvenWhenFrozenByAutomaticExceptDataTables()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new DataTableRecalcHarness();
            var (multiplier, bodyD2, bodyD3) = harness.CreateOneVariableDataTable();

            harness.SetCalculationMode(WorkbookCalculationMode.AutomaticExceptDataTables);
            harness.CommitCellEdit(multiplier, "5");
            harness.Sheet.GetCell(bodyD3)!.Value.Should().Be(new NumberValue(4), "frozen before Ctrl+Alt+F9");

            harness.InvokePrivateHandler("CalcFullBtn_Click");

            harness.Sheet.GetCell(bodyD2)!.Value.Should().Be(new NumberValue(5));
            harness.Sheet.GetCell(bodyD3)!.Value.Should().Be(new NumberValue(10));
        });
    }

    // Sibling coverage: Ctrl+Alt+Shift+F9 ("Rebuild Dependencies and Calculate") is the fourth
    // explicit forced-recalc entry point named in the same finding as F9/Shift+F9/Ctrl+Alt+F9 (see
    // MainWindow.WorkbookUiState.cs's RebuildDependenciesAndCalculate, bound via
    // KeyboardShortcutMatcher.CommandRules's Ctrl+Alt+Shift+F9 rule) and must get the identical
    // "always force every Data Table fresh" treatment as Ctrl+Alt+F9.
    [Fact]
    public void CtrlAltShiftF9RebuildDependencies_ForcesDataTableFreshEvenWhenFrozenByAutomaticExceptDataTables()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new DataTableRecalcHarness();
            var (multiplier, bodyD2, bodyD3) = harness.CreateOneVariableDataTable();

            harness.SetCalculationMode(WorkbookCalculationMode.AutomaticExceptDataTables);
            harness.CommitCellEdit(multiplier, "5");
            harness.Sheet.GetCell(bodyD3)!.Value.Should().Be(new NumberValue(4), "frozen before Ctrl+Alt+Shift+F9");

            harness.InvokePrivateHandler("RebuildDependenciesAndCalculate");

            harness.Sheet.GetCell(bodyD2)!.Value.Should().Be(new NumberValue(5));
            harness.Sheet.GetCell(bodyD3)!.Value.Should().Be(new NumberValue(10));
        });
    }

    private sealed class DataTableRecalcHarness : IDisposable
    {
        private readonly ICommandBus _commandBus;
        private readonly MethodInfo _commitEdit;
        private readonly MethodInfo _recalculateIfAutomatic;

        public MainWindow Window { get; }
        public Workbook Workbook { get; }
        public Sheet Sheet { get; }

        public DataTableRecalcHarness()
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            _commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
            Window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                _commandBus,
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance);

            Window.Show();
            PumpDispatcher();

            // MainWindow_Loaded (fired by Show() above) replaces the constructor-supplied workbook
            // with a fresh one via CreateNewWorkbook() -- capture the *live* workbook/sheet afterward
            // so the test operates on the same instances MainWindow's handlers use.
            Workbook = workbookRef.Current;
            Sheet = Workbook.GetSheetAt(0);

            _commitEdit = typeof(MainWindow)
                .GetMethod("CommitEdit", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CommitEdit");
            _recalculateIfAutomatic = typeof(MainWindow)
                .GetMethod("RecalculateIfAutomatic", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "RecalculateIfAutomatic");
        }

        /// <summary>
        /// Builds: A1 = multiplier (a plain value cell the Data Table's master formula reads besides
        /// its own input cell), B1 = the Data Table's input cell, D1 = the master formula "B1*A1",
        /// C2/C3 = trial input header values 1 and 2, and a one-variable, column-oriented Data Table
        /// (via the real <see cref="OneVariableDataTableCommand"/>, executed through the real
        /// <see cref="CommandBus"/> -- exactly what TryExecuteCommand wraps in production) whose body
        /// lands at D2/D3. Mirrors R118_AutomaticExceptDataTablesRecalcTests' layout in
        /// FreeX.App.Services.Tests so the two platforms are tested identically.
        /// </summary>
        public (CellAddress Multiplier, CellAddress BodyD2, CellAddress BodyD3) CreateOneVariableDataTable()
        {
            var multiplier = new CellAddress(Sheet.Id, 1, 1); // A1
            var inputCell = new CellAddress(Sheet.Id, 1, 2); // B1
            var tableFormula = new CellAddress(Sheet.Id, 1, 4); // D1

            Sheet.SetCell(multiplier, new NumberValue(2));
            Sheet.SetCell(inputCell, new NumberValue(0));
            Sheet.SetFormula(tableFormula, "B1*A1");
            Sheet.SetCell(new CellAddress(Sheet.Id, 2, 3), new NumberValue(1)); // C2
            Sheet.SetCell(new CellAddress(Sheet.Id, 3, 3), new NumberValue(2)); // C3

            // Seed D1's value via the real "Calculate Full" entry point before the Data Table is
            // created, mirroring how a real workbook would already have a computed master formula.
            InvokePrivateHandler("CalcFullBtn_Click");

            var createResult = _commandBus.Execute(
                Workbook.Id,
                new OneVariableDataTableCommand(
                    new GridRange(new CellAddress(Sheet.Id, 1, 3), new CellAddress(Sheet.Id, 3, 4)),
                    tableFormula,
                    inputCell,
                    DataTableInputOrientation.Column));
            createResult.Success.Should().BeTrue();

            // Mirrors the real production call site (MainWindow.DataCommands.cs's Data Table dialog
            // handler): TryExecuteCommand followed by RecalculateIfAutomatic(outcome.AffectedCells)
            // is what actually evaluates the freshly-created body cells' values for the first time.
            _recalculateIfAutomatic.Invoke(Window, [createResult.AffectedCells ?? Array.Empty<CellAddress>()]);

            return (multiplier, new CellAddress(Sheet.Id, 2, 4), new CellAddress(Sheet.Id, 3, 4));
        }

        public void SetCalculationMode(WorkbookCalculationMode mode)
        {
            _commandBus.Execute(Workbook.Id, new SetCalculationModeCommand(mode)).Success.Should().BeTrue();
        }

        /// <summary>
        /// Commits <paramref name="text"/> into <paramref name="address"/> via the real formula-bar
        /// commit path (<c>CommitEdit</c>, the same private method <c>FormulaBar_KeyDown</c>'s Enter
        /// handling and losing focus invoke), exercising <c>CommitPreparedEdits</c> ->
        /// <c>RecalculateIfAutomatic</c> exactly as an ordinary interactive edit would.
        /// </summary>
        public void CommitCellEdit(CellAddress address, string text)
        {
            ((SheetGridView)Window.FindName("SheetGrid")).SelectedRange = new GridRange(address, address);
            ((TextBox)Window.FindName("FormulaBar")).Text = text;
            ((bool)_commitEdit.Invoke(Window, null)!).Should().BeTrue();
            PumpDispatcher();
        }

        public void InvokePrivateHandler(string methodName) =>
            DialogSourceTestSupport.InvokePrivateHandler(Window, methodName);

        public void Dispose()
        {
            foreach (System.Windows.Window ownedWindow in Window.OwnedWindows.Cast<System.Windows.Window>().ToList())
                ownedWindow.Close();
            MainWindowTestCleanup.CloseWithoutSavePrompt(Window);
            PumpDispatcher();
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
