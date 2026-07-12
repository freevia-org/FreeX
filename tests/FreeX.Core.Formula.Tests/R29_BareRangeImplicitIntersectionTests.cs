using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R29-formula-array-eval-deep-1: a bare top-level range reference evaluated in a legacy
/// (non-array-entered) formula context — i.e. via <see cref="FormulaEvaluator.Evaluate"/>, the
/// method RecalcEngine calls for <c>FormulaArrayMode.Implicit</c> cells (see RecalcEngine.cs:224-226)
/// — must implicitly intersect against the formula cell's own row/column (Excel's legacy behaviour),
/// not always collapse to the range's top-left cell. FormulaEvaluator.References.cs's EvaluateRange
/// is the method backing this dispatch (FormulaEvaluator.cs:184-186).
/// </summary>
public sealed class R29_BareRangeImplicitIntersectionTests
{
    private readonly FormulaEvaluator _evaluator = new();

    private static Sheet MakeColumnSheet()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        for (uint row = 1; row <= 10; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row * 10));
        return sheet;
    }

    [Fact]
    public void BareColumnRange_IntersectsFormulaCellsOwnRow_NotTopLeft()
    {
        var sheet = MakeColumnSheet();

        // Formula cell is row 5 of the same column the range spans (A1:A10). Real Excel reads A5
        // (=50), not A1 (=10) — the bug this test guards collapsed to the top-left cell instead.
        var result = _evaluator.Evaluate("=A1:A10", sheet, currentCell: new CellAddress(sheet.Id, 5, 1));

        result.Should().Be(new NumberValue(50));
    }

    [Fact]
    public void BareFullColumnRange_IntersectsFormulaCellsOwnRow()
    {
        var sheet = MakeColumnSheet();

        // Same scenario via a full-column reference (=A:A), the other shape the finding calls out.
        var result = _evaluator.Evaluate("=A:A", sheet, currentCell: new CellAddress(sheet.Id, 5, 1));

        result.Should().Be(new NumberValue(50));
    }

    [Fact]
    public void BareRowRange_IntersectsFormulaCellsOwnColumn_RegardlessOfRow()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        for (uint col = 1; col <= 10; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new NumberValue(col * 100));

        // A single-row range intersects purely on column — the formula cell's row (99) is
        // irrelevant and must not force an off-axis #VALUE!.
        var result = _evaluator.Evaluate("=A1:J1", sheet, currentCell: new CellAddress(sheet.Id, 99, 5));

        result.Should().Be(new NumberValue(500));
    }

    [Fact]
    public void BareColumnRange_ReversedRange_StillNormalizesAndIntersects()
    {
        var sheet = MakeColumnSheet();

        // Excel normalizes a reversed range (A10:A1 => A1:A10) before intersecting — pre-existing
        // reversed-range logic must survive alongside the new intersection behaviour.
        var result = _evaluator.Evaluate("=A10:A1", sheet, currentCell: new CellAddress(sheet.Id, 5, 1));

        result.Should().Be(new NumberValue(50));
    }

    [Fact]
    public void BareColumnRange_FormulaCellOffAxis_ReturnsValueError()
    {
        var sheet = MakeColumnSheet();

        // Formula cell's row (50) falls entirely outside the referenced range (A1:A10) -> #VALUE!,
        // matching Excel and the already-tested behaviour of the explicit @ operator
        // (Backlog_ImplicitIntersectionTests.At_OnReferenceRange_OffAxis_ReturnsValueError).
        var result = _evaluator.Evaluate("=A1:A10", sheet, currentCell: new CellAddress(sheet.Id, 50, 1));

        result.Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void BareRange_NoCurrentCellContext_StillReturnsTopLeftCell()
    {
        var sheet = MakeColumnSheet();

        // Sibling (already-working) case: a direct Evaluate(text, sheet) call with no currentCell
        // (e.g. FormulaEvaluationSummaryService's context-free evaluation) has no formula-cell
        // position to intersect against, so it must keep the historical top-left-cell reading.
        var result = _evaluator.Evaluate("=A1:A10", sheet);

        result.Should().Be(new NumberValue(10));
    }
}
