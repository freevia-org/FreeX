using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// R32-meta-3: the nested-SUBTOTAL/AGGREGATE detection scans a cell's raw FormulaText for
// "SUBTOTAL(" / "AGGREGATE(" with no string-literal awareness, so a formula whose SOURCE merely
// contains that substring inside a quoted string literal (pure string concatenation, never an
// actual nested call) was wrongly excluded from an enclosing SUBTOTAL/AGGREGATE's aggregation.
public partial class FunctionLibraryTests
{
    [Fact]
    public void Subtotal_FuncNum9_StringLiteralContainingSubtotalText_IsNotTreatedAsNested()
    {
        // A1's formula only ever builds a text value by concatenation -- it never actually calls
        // SUBTOTAL. The substring "SUBTOTAL(" appears solely inside quoted string literals, so
        // A1 must still be included in A2's aggregation.
        var sheet = MakeSheet((3, 1, new NumberValue(30)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell
        {
            FormulaText = "\"Regional total: \"&\"SUBTOTAL(\"&\"9,B1:B2)\"",
            Value = new TextValue("Regional total: SUBTOTAL(9,B1:B2)")
        });
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell
        {
            FormulaText = "\"\"\"SUBTOTAL(\"\"\"", // string literal with escaped quotes still containing the substring
            Value = new TextValue("\"SUBTOTAL(\"")
        });
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new Cell
        {
            FormulaText = "30",
            Value = new NumberValue(30)
        });

        // Only A3 is numeric; A1/A2 are text (no real nested call), so they contribute 0 to SUM
        // but must NOT be skipped as "nested" -- this is really exercised via COUNTA below.
        _eval.Evaluate("=SUBTOTAL(9,A1:A3)", sheet).Should().Be(new NumberValue(30));
        _eval.Evaluate("=SUBTOTAL(3,A1:A3)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Subtotal_FuncNum9_GenuineNestedSubtotalCall_StillExcluded()
    {
        // Sibling case: a genuine "=1+SUBTOTAL(...)" nested call (not inside a string literal)
        // must still be recognized as nested and excluded -- the string-literal fix must not
        // over-correct and start including real nested calls.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (3, 1, new NumberValue(30)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell
        {
            FormulaText = "1+SUBTOTAL(9,A1:A1)",
            Value = new NumberValue(11)
        });

        _eval.Evaluate("=SUBTOTAL(9,A1:A3)", sheet).Should().Be(new NumberValue(40));
    }

    [Fact]
    public void Subtotal_FuncNum4_AllTextRange_EmptyRangeBehaviorUnchanged()
    {
        // Sibling case: the empty-range-returns-0 behavior (R31) is unaffected by the
        // string-literal-aware scan.
        var sheet = MakeSheet((1, 1, new TextValue("hello")));
        _eval.Evaluate("=SUBTOTAL(4,A1:A1)", sheet).Should().Be(new NumberValue(0));
    }
}

public partial class PhaseA2FunctionTests
{
    [Fact]
    public void Aggregate_Sum_Option0_StringLiteralContainingSubtotalText_IsNotTreatedAsNested()
    {
        var (wb, sheet) = MakeWb((3, 1, new NumberValue(30)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell
        {
            FormulaText = "\"Regional total: \"&\"SUBTOTAL(\"&\"9,B1:B2)\"",
            Value = new TextValue("Regional total: SUBTOTAL(9,B1:B2)")
        });
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell
        {
            FormulaText = "20",
            Value = new NumberValue(20)
        });

        _eval.Evaluate("=AGGREGATE(9,0,A1:A3)", sheet, wb).Should().Be(new NumberValue(50));
    }

    [Fact]
    public void Aggregate_Sum_Option0_GenuineNestedSubtotalCall_StillExcluded()
    {
        // Sibling case: genuine nested calls remain excluded.
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(10)),
            (3, 1, new NumberValue(30)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell
        {
            FormulaText = "1+SUBTOTAL(9,A1:A1)",
            Value = new NumberValue(11)
        });

        _eval.Evaluate("=AGGREGATE(9,0,A1:A3)", sheet, wb).Should().Be(new NumberValue(40));
    }
}
