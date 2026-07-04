using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for the P-ref-functions review group:
///   H54 - OFFSET's base-reference argument must accept nested reference-returning function calls
///         (OFFSET/INDIRECT), not just literal cell/range/named-range AST nodes.
///   H63 - INDIRECT's sheet-qualifier split must find the '!' that terminates a (possibly quoted)
///         sheet name, not just the first '!' in the text — quoted sheet names may themselves
///         contain '!'.
/// (P1's INDIRECT-full-column-clamp regression tests live alongside the existing INDIRECT clamp
/// tests in FunctionLibraryTests.LogicalInformation.cs.)
/// </summary>
public class PRefFunctionsGroupTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    // ── H54: OFFSET base argument accepts nested reference-returning function calls ──────────

    [Fact]
    public void Offset_BaseArgumentIsIndirectCall_ResolvesNestedReference()
    {
        var sheet = MakeSheet((2, 2, new NumberValue(42))); // B2

        // OFFSET(INDIRECT("A1"),1,1) -> base is A1, offset by (1,1) -> B2.
        _eval.Evaluate("=OFFSET(INDIRECT(\"A1\"),1,1)", sheet).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void Offset_BaseArgumentIsNestedOffsetCall_ResolvesNestedReference()
    {
        var sheet = MakeSheet((2, 2, new NumberValue(7))); // B2

        // OFFSET(OFFSET(A1,0,0),1,1) -> inner OFFSET resolves to A1, outer offsets to B2.
        _eval.Evaluate("=OFFSET(OFFSET(A1,0,0),1,1)", sheet).Should().Be(new NumberValue(7));
    }

    [Fact]
    public void Offset_BaseArgumentIsIndirectRange_ResolvesNestedRangeAndKeepsDimensions()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (2, 2, new NumberValue(20)),
            (3, 2, new NumberValue(30)));

        // Base is A1:A2 (1 col x 2 rows); offset by (1,1) with dimensions preserved -> B2:B3.
        _eval.Evaluate("=SUM(OFFSET(INDIRECT(\"A1:A2\"),1,1))", sheet).Should().Be(new NumberValue(50));
    }

    [Fact]
    public void Offset_BaseArgumentIndirectError_PropagatesErrorInsteadOfValueError()
    {
        var sheet = MakeSheet();

        // INDIRECT("ZZ999999999") is not a valid reference -> #REF! should propagate, not be masked
        // by the generic "unsupported base argument" #VALUE!.
        _eval.Evaluate("=OFFSET(INDIRECT(\"NotASheet!A1\"),0,0)", sheet).Should().Be(ErrorValue.Ref);
    }

    // ── H63: INDIRECT sheet-qualifier split uses the correct (quote-aware) '!' ────────────────

    [Fact]
    public void Indirect_QuotedSheetNameContainingBang_ResolvesCorrectly()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Q1!Summary"); // '!' is a legal sheet-name character
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(99));

        var result = _eval.Evaluate("=INDIRECT(\"'Q1!Summary'!A1\")", sheet1, workbook);

        result.Should().Be(new NumberValue(99));
    }

    [Fact]
    public void Indirect_QuotedSheetNameContainingBangAndEscapedApostrophe_ResolvesCorrectly()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Bob's!Sheet");
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(7));

        // Excel's own quoting rule: an embedded apostrophe is doubled ('').
        var result = _eval.Evaluate("=INDIRECT(\"'Bob''s!Sheet'!A1\")", sheet1, workbook);

        result.Should().Be(new NumberValue(7));
    }

    [Fact]
    public void Indirect_UnquotedSimpleSheetName_StillResolvesCorrectly()
    {
        // Guard against a regression in the ordinary (single '!', unquoted) case.
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(5));

        _eval.Evaluate("=INDIRECT(\"Sheet2!A1\")", sheet1, workbook).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Indirect_MalformedMultiBangReference_StillReturnsRef()
    {
        // Sanity check: a genuinely malformed reference (embedded, unquoted '!' that isn't a
        // legal sheet qualifier) must still fail with #REF!, not silently misparse.
        var sheet = MakeSheet();
        _eval.Evaluate("=INDIRECT(\"Sheet1!A1!B2\")", sheet).Should().Be(ErrorValue.Ref);
    }
}
