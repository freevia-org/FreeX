using System.Reflection;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R121-app-host-goalseek-validation-gap: <c>FreeX.App.Services.WorkbookCellEditService</c>'s
/// private <c>TryValidateGoalSeekRequest</c> refuses to run Goal Seek when the Set cell holds no
/// formula, or when the Changing cell already holds a formula (R90-app-goalseek-whatif-5-1/5-2) --
/// the latter guard exists specifically because <see cref="GoalSeekCommand"/>.Apply unconditionally
/// overwrites the changing cell with a plain <see cref="NumberValue"/>, with no formula check of its
/// own, so skipping the guard silently destroys whatever formula was there. The WPF host's own
/// <c>GoalSeekBtn_Click</c> (MainWindow.DataCommands.cs) drove <c>GoalSeekInputParser</c> ->
/// <c>GoalSeekRequestParser</c>, which validates only cell-reference SYNTAX and that Set/Changing
/// differ -- never what the two cells actually contain -- so neither guard existed on this
/// platform. This test drives <c>MainWindow.TryValidateGoalSeekCells</c> (extracted so it is testable
/// without the two modal dialogs <c>GoalSeekBtn_Click</c> also shows) directly by reflection.
/// </summary>
public sealed class R121_GoalSeekValidationGapTests
{
    [Fact]
    public void ChangingCellHoldsAFormula_IsRejected()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create();

            // Set cell (B1) has a formula; Changing cell (A1) ALSO has a formula -- Excel refuses
            // this because GoalSeekCommand would otherwise silently overwrite A1's formula with a
            // plain number.
            harness.SetCellFormula(1, 1, "5"); // A1 = "=5" (a formula, deliberately)
            harness.SetCellFormula(2, 1, "A1*2"); // B1 = "=A1*2"

            var setCell = new CellAddress(harness.CurrentSheetId, 2, 1); // B1
            var changingCell = new CellAddress(harness.CurrentSheetId, 1, 1); // A1

            InvokeTryValidateGoalSeekCells(harness, setCell, changingCell, out var valid, out var error);

            valid.Should().BeFalse("the changing cell already holds a formula and must not be silently overwritten");
            error.Should().Contain("By changing cell");

            // No-regression: the formula must still be intact (this call must not itself mutate
            // anything -- it is a pure validation gate).
            harness.CellFormula(1, 1).Should().Be("5");
        });
    }

    [Fact]
    public void SetCellHasNoFormula_IsRejected()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create();

            harness.SetCellNumber(2, 1, 42); // B1 = 42 (plain value, no formula)
            harness.SetCellNumber(1, 1, 5); // A1 = 5 (plain value, valid changing cell)

            var setCell = new CellAddress(harness.CurrentSheetId, 2, 1); // B1
            var changingCell = new CellAddress(harness.CurrentSheetId, 1, 1); // A1

            InvokeTryValidateGoalSeekCells(harness, setCell, changingCell, out var valid, out var error);

            valid.Should().BeFalse("the set cell must contain a formula for Goal Seek to have anything to drive toward a target");
            error.Should().Contain("Set cell");
        });
    }

    // No-regression sibling: a properly-shaped request (Set cell has a formula, Changing cell is a
    // plain value) must still be accepted.
    [Fact]
    public void ValidRequest_SetCellHasFormulaAndChangingCellIsAConstant_IsAccepted()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create();

            harness.SetCellNumber(1, 1, 5); // A1 = 5 (constant changing cell)
            harness.SetCellFormula(2, 1, "A1*2"); // B1 = "=A1*2" (formula set cell)

            var setCell = new CellAddress(harness.CurrentSheetId, 2, 1); // B1
            var changingCell = new CellAddress(harness.CurrentSheetId, 1, 1); // A1

            InvokeTryValidateGoalSeekCells(harness, setCell, changingCell, out var valid, out var error);

            valid.Should().BeTrue(error);
        });
    }

    private static void InvokeTryValidateGoalSeekCells(
        MainWindowFormulaBarSyncTests.MainWindowHarness harness,
        CellAddress setCell,
        CellAddress changingCell,
        out bool valid,
        out string? error)
    {
        var method = typeof(MainWindow).GetMethod(
            "TryValidateGoalSeekCells",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "TryValidateGoalSeekCells");

        var args = new object?[] { setCell, changingCell, null };
        valid = (bool)method.Invoke(harness.Window, args)!;
        error = (string?)args[2];
    }
}
