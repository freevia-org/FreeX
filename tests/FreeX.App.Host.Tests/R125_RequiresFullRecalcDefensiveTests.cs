using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Pins the shared <see cref="WorkbookCellEditService"/> recalc dispatch now used by every
/// renderer. A flagged history outcome forces a workbook-wide recalculation; an unflagged outcome
/// remains targeted to its affected cells.
/// </summary>
public sealed class R125_RequiresFullRecalcDefensiveTests
{
    [Fact]
    public void RequiresFullRecalc_WithNonEmptyAffectedCells_ForcesFullRecalcNotTargeted()
    {
        var fixture = CreateFixture();
        var a1 = new CellAddress(fixture.Sheet.Id, 1, 1);
        var b1 = new CellAddress(fixture.Sheet.Id, 1, 2);
        fixture.CommandBus.Outcome = new CommandOutcome(
            true,
            AffectedCells: [a1],
            RequiresFullRecalc: true);
        SeedUnevaluatedFormula(fixture.Sheet, a1, b1);

        fixture.Service.UndoLastEdit(fixture.Workbook).Success.Should().BeTrue();

        fixture.Sheet.GetValue(b1).Should().Be(new NumberValue(6),
            "RequiresFullRecalc=true must force a full recalc even though AffectedCells is non-empty");
    }

    [Fact]
    public void RequiresFullRecalcFalse_WithNonEmptyAffectedCells_StaysTargeted()
    {
        var fixture = CreateFixture();
        var a1 = new CellAddress(fixture.Sheet.Id, 1, 1);
        var b1 = new CellAddress(fixture.Sheet.Id, 1, 2);
        fixture.CommandBus.Outcome = new CommandOutcome(
            true,
            AffectedCells: [a1],
            RequiresFullRecalc: false);
        SeedUnevaluatedFormula(fixture.Sheet, a1, b1);

        fixture.Service.UndoLastEdit(fixture.Workbook).Success.Should().BeTrue();

        fixture.Sheet.GetValue(b1).Should().Be(BlankValue.Instance,
            "a targeted recalc of A1 alone must not reach B1's unregistered dependency");
    }

    [Fact]
    public void EmptyAffectedCells_WithoutFullRecalcFlag_DoesNotInferAFullRecalc()
    {
        var fixture = CreateFixture();
        fixture.CommandBus.Outcome = new CommandOutcome(
            true,
            AffectedCells: [],
            RequiresFullRecalc: false);
        var a1 = new CellAddress(fixture.Sheet.Id, 1, 1);
        var b1 = new CellAddress(fixture.Sheet.Id, 1, 2);
        SeedUnevaluatedFormula(fixture.Sheet, a1, b1);

        fixture.Service.UndoLastEdit(fixture.Workbook).Success.Should().BeTrue();

        fixture.Sheet.GetValue(b1).Should().Be(BlankValue.Instance,
            "shared command ownership uses RequiresFullRecalc, not an empty affected-cell list, " +
            "to identify structural workbook recalculation");
    }

    private static RecalcFixture CreateFixture()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var commandBus = new OutcomeCommandBus();
        var service = new WorkbookCellEditService(
            commandBus,
            new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()));
        return new RecalcFixture(workbook, sheet, commandBus, service);
    }

    private static void SeedUnevaluatedFormula(Sheet sheet, CellAddress a1, CellAddress b1)
    {
        sheet.SetCell(a1, Cell.FromValue(new NumberValue(5)));
        sheet.SetCell(b1, Cell.FromFormula("=A1+1"));
        sheet.GetValue(b1).Should().Be(BlankValue.Instance,
            "the dependency graph has not discovered the raw formula yet");
    }

    private sealed record RecalcFixture(
        Workbook Workbook,
        Sheet Sheet,
        OutcomeCommandBus CommandBus,
        WorkbookCellEditService Service);

    private sealed class OutcomeCommandBus : ICommandBus
    {
        public CommandOutcome Outcome { get; set; } = new(false, "No outcome configured.");

        public CommandOutcome Execute(WorkbookId workbookId, IWorkbookCommand command) =>
            throw new NotSupportedException();

        public CommandOutcome ExecuteRepeatable(
            WorkbookId workbookId,
            Func<IWorkbookCommand> commandFactory) =>
            throw new NotSupportedException();

        public CommandOutcome Undo(WorkbookId workbookId) => Outcome;

        public CommandOutcome Redo(WorkbookId workbookId) =>
            throw new NotSupportedException();

        public bool CanUndo(WorkbookId workbookId) => true;

        public bool CanRedo(WorkbookId workbookId) => false;

        public CommandOutcome RepeatLast(WorkbookId workbookId) =>
            throw new NotSupportedException();

        public bool CanRepeat(WorkbookId workbookId) => false;

        public int GetUndoStackDepth(WorkbookId workbookId) => 1;
    }
}
