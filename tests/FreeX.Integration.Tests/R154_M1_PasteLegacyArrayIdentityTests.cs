using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R154/meta-F1: PasteCommandCellFactory's BuildAllCell (ordinary Ctrl+V, PasteCellsMode.All) and
/// BuildFormulaOrValueCell (PasteCellsMode.Formulas / "Formulas and number formats" Paste Special)
/// both cloned the source cell (which correctly preserves ArrayMode/LegacyArrayRows/LegacyArrayCols,
/// see Cell.Clone) and then immediately reassigned <c>pastedCell.FormulaText</c> to the rewritten
/// reference text -- silently undoing what Clone() had just preserved, because the FormulaText setter
/// (Cell.cs) unconditionally resets those three properties on every assignment. Fixed by routing both
/// reassignments through RowColumnShiftHelpers.SetFormulaTextPreservingArrayIdentity, exactly like the
/// R153/K1 remediation already did for MoveRangeCommand/DuplicateSheetCommand's equivalent loops.
/// </summary>
public sealed class R154_M1_PasteLegacyArrayIdentityTests
{
    private static (Workbook workbook, Sheet sheet, CellAddress h1) BuildWorkbookWithLegacyArrayAnchor()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint r = 10; r <= 14; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 4), new NumberValue(r - 9)); // D10:D14 = 1..5

        var h1 = new CellAddress(sheet.Id, 1, 8);
        var legacyCell = Cell.FromFormula("SUM(D10:D14)");
        legacyCell.ArrayMode = FormulaArrayMode.Implicit;
        legacyCell.LegacyArrayRows = 2;
        legacyCell.LegacyArrayCols = 1;
        sheet.SetCell(h1, legacyCell);
        return (workbook, sheet, h1);
    }

    [Fact]
    public void OrdinaryCtrlV_OfLegacyArrayFormulaCell_PreservesArrayIdentityAndGuard()
    {
        var (workbook, sheet, h1) = BuildWorkbookWithLegacyArrayAnchor();
        var ctx = new TestCommandContext(workbook);

        var sourceCell = sheet.GetCell(h1)!;
        var k1 = new CellAddress(sheet.Id, 1, 11); // K1: rowDelta 0, colDelta +3

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            workbook,
            sheet.Id,
            new GridRange(h1, h1),
            [(h1, sourceCell.Clone())],
            k1,
            PasteCellsMode.All,
            default);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var pasted = sheet.GetCell(k1)!;
        pasted.FormulaText.Should().Be("SUM(G10:G14)", "the reference must follow the paste's column offset");
        pasted.LegacyArrayRows.Should().Be(2u,
            "an ordinary Ctrl+V of a legacy CSE array formula must not strip its fixed-extent identity");
        pasted.LegacyArrayCols.Should().Be(1u);
        pasted.ArrayMode.Should().Be(FormulaArrayMode.Implicit);

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RebuildFormulaDependencies(workbook);
        engine.Recalculate(workbook, [k1]);

        CommandGuards.RejectIfSplitsArray(sheet, [new CellAddress(sheet.Id, 2, 11)]).Should().NotBeNull(
            "'You cannot change part of an array' must still guard the pasted array's second declared cell");
    }

    [Fact]
    public void OrdinaryCtrlV_OfOrdinaryFormulaCell_StillRewritesAndStaysDynamic_NoRegression()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(7));
        sheet.SetFormula(b1, "A1*2");

        var d1 = new CellAddress(sheet.Id, 1, 4);
        var command = PasteCommandFactory.CreateInternalPasteCommand(
            workbook,
            sheet.Id,
            new GridRange(b1, b1),
            [(b1, sheet.GetCell(b1)!.Clone())],
            d1,
            PasteCellsMode.All,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.GetCell(d1)!;
        pasted.FormulaText.Should().Be("C1*2");
        pasted.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);
        pasted.LegacyArrayRows.Should().Be(0u);
        pasted.LegacyArrayCols.Should().Be(0u);
    }

    [Fact]
    public void PasteFormulasOnly_OfLegacyArrayFormulaCell_PreservesArrayIdentity()
    {
        var (workbook, sheet, h1) = BuildWorkbookWithLegacyArrayAnchor();
        var ctx = new TestCommandContext(workbook);

        var sourceCell = sheet.GetCell(h1)!;
        var k1 = new CellAddress(sheet.Id, 1, 11);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            workbook,
            sheet.Id,
            new GridRange(h1, h1),
            [(h1, sourceCell.Clone())],
            k1,
            PasteCellsMode.Formulas,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.GetCell(k1)!;
        pasted.FormulaText.Should().Be("SUM(G10:G14)");
        pasted.LegacyArrayRows.Should().Be(2u,
            "PasteCellsMode.Formulas (BuildFormulaOrValueCell) must also preserve legacy array identity");
        pasted.LegacyArrayCols.Should().Be(1u);
    }
}
