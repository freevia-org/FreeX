using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression coverage for the W-spill-anchor-shapes group (finding J49): Excel's A1#:B5
/// (spill range as one endpoint of a larger range) and NamedRange# (spill anchor via a named
/// range pointing at a single cell) were previously unparseable — A1#:B5 threw a top-level
/// "Unexpected token ':'" FormulaParseException, and NamedRange# threw "Unexpected '#'" from
/// WrapSpillAnchor's hard CellRefNode-only type check.
/// </summary>
public class WSpillAnchorShapesTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheetWithSpill(uint anchorRow, uint anchorCol, params double[] spillValues)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        var anchorAddr = new CellAddress(sheet.Id, anchorRow, anchorCol);
        sheet.SetCell(anchorAddr, Cell.FromValue(new NumberValue(spillValues[0])));
        var cells = new ScalarValue[spillValues.Length, 1];
        for (var i = 0; i < spillValues.Length; i++)
            cells[i, 0] = new NumberValue(spillValues[i]);
        var rv = new RangeValue(cells, anchorRow, anchorCol);
        sheet.SetSpillRange(anchorAddr, rv);
        return sheet;
    }

    [Fact]
    public void SpillRangeToCell_ParsesWithoutThrowing()
    {
        // A1#:B5 must no longer blow up the top-level "Unexpected token ':'" parse error.
        var ast = new Parser(new Lexer("A1#:B5").Tokenize()).Parse();

        ast.Should().BeOfType<FunctionCallNode>();
        var call = (FunctionCallNode)ast;
        call.FunctionName.Should().Be("ANCHORARRAY");
        call.Arguments.Should().HaveCount(2);
        call.Arguments[0].Should().BeOfType<CellRefNode>();
        call.Arguments[1].Should().BeOfType<CellRefNode>();
    }

    [Fact]
    public void SpillRangeToCell_UnionsSpillExtentWithEndCell()
    {
        // A1 spills a 3x1 range (A1:A3); A1#:B5 must expand to the union of that spill extent
        // and B5, i.e. the smallest rectangle covering both -> A1:B5.
        var sheet = MakeSheetWithSpill(1, 1, 1, 2, 3);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), Cell.FromValue(new NumberValue(99)));

        var result = _eval.Evaluate("=SUM(A1#:B5)", sheet);

        // Sum of the spill (1+2+3) plus B5 (99); every other cell in the A1:B5 rectangle is blank.
        result.Should().Be(new NumberValue(1 + 2 + 3 + 99));
    }

    [Fact]
    public void SpillRangeToCell_ReturnsExpectedRectangleShape()
    {
        var sheet = MakeSheetWithSpill(1, 1, 1, 2, 3);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), Cell.FromValue(new NumberValue(99)));

        var result = _eval.Evaluate("=A1#:B5", sheet);

        var range = result.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(5); // rows 1..5
        range.ColCount.Should().Be(2); // cols A..B
        range.Cells[0, 0].Should().Be(new NumberValue(1));
        range.Cells[1, 0].Should().Be(new NumberValue(2));
        range.Cells[2, 0].Should().Be(new NumberValue(3));
        range.Cells[4, 1].Should().Be(new NumberValue(99)); // B5
    }

    [Fact]
    public void SpillRangeToCell_AnchorNotASpill_ReturnsRefError()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(42)));

        var result = _eval.Evaluate("=A1#:B5", sheet);

        result.Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void NamedRangeSpillAnchor_ParsesAndEvaluatesFullSpillRange()
    {
        // MyCell# where MyCell is a named range pointing at a single cell that is a spill anchor.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var anchorAddr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(anchorAddr, Cell.FromValue(new NumberValue(1)));
        var rv = new RangeValue(new ScalarValue[,]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) },
        }, 1, 1);
        sheet.SetSpillRange(anchorAddr, rv);
        workbook.DefineNamedRange("MyCell", new GridRange(anchorAddr, anchorAddr));

        var result = _eval.Evaluate("=SUM(MyCell#)", sheet, workbook);

        result.Should().Be(new NumberValue(6));
    }

    [Fact]
    public void NamedRangeSpillAnchor_MultiCellName_ReturnsValueErrorNotThrow()
    {
        // A named range that spans more than one cell can't be a spill anchor -> #VALUE!, not a crash.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(2)));
        workbook.DefineNamedRange("MyRange",
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)));

        var result = _eval.Evaluate("=MyRange#", sheet, workbook);

        result.Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void PlainCellSpillAnchor_StillParsesAndSerializesRoundTrip()
    {
        // Guard against regressing the pre-existing A1# shape while adding the new ones.
        var ast = new Parser(new Lexer("A1#").Tokenize()).Parse();
        var serialized = FormulaSerializer.Serialize(ast);

        serialized.Should().Be("A1#");
    }

    [Fact]
    public void SpillRangeToCell_SerializesRoundTrip()
    {
        var ast = new Parser(new Lexer("A1#:B5").Tokenize()).Parse();
        var serialized = FormulaSerializer.Serialize(ast);

        serialized.Should().Be("A1#:B5");
    }

    [Fact]
    public void NamedRangeSpillAnchor_SerializesRoundTrip()
    {
        var ast = new Parser(new Lexer("MyCell#").Tokenize()).Parse();
        var serialized = FormulaSerializer.Serialize(ast);

        serialized.Should().Be("MyCell#");
    }
}
