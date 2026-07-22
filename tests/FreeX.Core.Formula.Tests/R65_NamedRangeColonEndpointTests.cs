using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R65-formula-reference-ops-6-2: a defined NAME used as one endpoint of the ':' range operator
/// (e.g. <c>name:cellref</c>, <c>cellref:name</c>, <c>name:name</c> -- e.g.
/// <c>=SUM(StartCell:B2)</c> where StartCell refers to A1) failed to parse and surfaced #VALUE!.
/// Parser.ParsePrimary's NamedRange case fell through with no Colon check after consuming the
/// name, and Parser.ParseIndexRangeEndpoint (the endpoint parser reused by the plain-CellRef ':'
/// path) rejected a NamedRange endpoint token outright. Fixed by recognizing a following ':' in
/// both places and building a <see cref="NamedRangeEndpointNode"/> whose Start/End are each either
/// a CellRefNode or a NamedRangeNode; the evaluator resolves any NamedRangeNode endpoint to its
/// defined range's top-left cell (Excel always anchors on the name's corner) before forming the
/// effective range.
/// </summary>
public sealed class R65_NamedRangeColonEndpointTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void NameColonCellRef_ResolvesNameToItsCornerAndSumsRange()
    {
        // StartCell -> A1. A1:B2 = {A1,B1,A2,B2}; only A1=1 and B2=99 are populated (others
        // blank/0), so the range sum is exactly 100 -- the finding's exact worked example.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(b2, new NumberValue(99));
        workbook.DefineNamedRange("StartCell", new GridRange(a1, a1));

        var result = _eval.Evaluate("=SUM(StartCell:B2)", sheet, workbook);

        result.Should().Be(new NumberValue(100));
    }

    [Fact]
    public void CellRefColonName_ResolvesNameToItsCornerAndSumsRange()
    {
        // A1:EndName, EndName -> C3, over a 3x3 grid filled 1..9 -> sum 45.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var n = 1;
        for (uint r = 1; r <= 3; r++)
            for (uint c = 1; c <= 3; c++)
                sheet.SetCell(new CellAddress(sheet.Id, r, c), new NumberValue(n++));
        var c3 = new CellAddress(sheet.Id, 3, 3);
        workbook.DefineNamedRange("EndName", new GridRange(c3, c3));

        var result = _eval.Evaluate("=SUM(A1:EndName)", sheet, workbook);

        result.Should().Be(new NumberValue(45));
    }

    [Fact]
    public void NameColonName_ResolvesBothNamesToTheirCorners()
    {
        // StartCell -> A1, EndName -> C3, over the same 3x3 grid filled 1..9 -> sum 45.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var n = 1;
        for (uint r = 1; r <= 3; r++)
            for (uint c = 1; c <= 3; c++)
                sheet.SetCell(new CellAddress(sheet.Id, r, c), new NumberValue(n++));
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c3 = new CellAddress(sheet.Id, 3, 3);
        workbook.DefineNamedRange("StartCell", new GridRange(a1, a1));
        workbook.DefineNamedRange("EndName", new GridRange(c3, c3));

        var result = _eval.Evaluate("=SUM(StartCell:EndName)", sheet, workbook);

        result.Should().Be(new NumberValue(45));
    }

    [Fact]
    public void NameColonEndpoint_MultiCellName_UsesTopLeftCorner()
    {
        // A defined name that itself refers to a multi-cell range uses its TOP-LEFT corner as the
        // range endpoint (matching Excel), not the whole range or its bottom-right.
        // BlockName -> B2:C3 (top-left corner B2). BlockName:D4 must therefore be B2:D4.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint r = 1; r <= 4; r++)
            for (uint c = 1; c <= 4; c++)
                sheet.SetCell(new CellAddress(sheet.Id, r, c), new NumberValue(r * 10 + c));
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var c3 = new CellAddress(sheet.Id, 3, 3);
        workbook.DefineNamedRange("BlockName", new GridRange(b2, c3));

        var result = _eval.Evaluate("=SUM(BlockName:D4)", sheet, workbook);

        // B2:D4 = 22+23+24+32+33+34+42+43+44 = 297
        result.Should().Be(new NumberValue(297));
    }

    [Fact]
    public void NameColonEndpoint_UndefinedName_ReturnsNameError()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        var result = _eval.Evaluate("=SUM(NoSuchName:B2)", sheet, workbook);

        result.Should().Be(ErrorValue.Name);
    }

    // --- No-regression siblings -------------------------------------------------------------

    [Fact]
    public void PlainNamedRange_WithNoColonEndpoint_StillEvaluatesUnchanged()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(b2, new NumberValue(99));
        workbook.DefineNamedRange("PlainRange", new GridRange(a1, b2));

        var result = _eval.Evaluate("=SUM(PlainRange)", sheet, workbook);

        result.Should().Be(new NumberValue(100));
    }

    [Fact]
    public void PlainCellRefColonCellRefRange_StillWorksUnchanged()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var n = 1;
        for (uint r = 1; r <= 3; r++)
            for (uint c = 1; c <= 3; c++)
                sheet.SetCell(new CellAddress(sheet.Id, r, c), new NumberValue(n++));

        var result = _eval.Evaluate("=SUM(A1:C3)", sheet, workbook);

        result.Should().Be(new NumberValue(45));
    }
}
