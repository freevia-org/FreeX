using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-56 formula-bucket regression tests:
///
/// R56-formula-xlookup-modes-5-1: XlookupRangeLookupValues (BuiltInFunctions.Lookup.Modern.cs)
/// rejected a genuine 1x1-range match_mode/search_mode argument for an array lookup_value,
/// instead of broadcasting it the same way MapTernaryTextArgs/CanBroadcastToShape already do for
/// XLOOKUP's scalar-lookup_value path and for XMATCH's array path.
///
/// R56-formula-information-fns-5-1: ISFORMULA/FORMULATEXT's shared reference-argument resolver
/// (TryResolveReferenceTopLeftCell, FormulaEvaluator.References.cs) didn't recognize INDEX/CHOOSE
/// as reference-returning, unlike the r55 fix already applied to ISREF/CELL.
///
/// R56-formula-legacy-array-cse-5-1: EvaluateNamedRange (FormulaEvaluator.References.cs) returned
/// the raw multi-cell RangeValue for a bare named-range reference, skipping the same current-cell
/// implicit intersection EvaluateRange already applies to a bare cell-range reference -- breaking
/// Data-Validation/Conditional-Format formulas of the form "=Name" that must narrow to the row/
/// column being evaluated.
///
/// R56-formula-financial-tvm-5-1: RATE's guess argument (BuiltInFunctions.Financial.LoanValues.cs)
/// treated an explicitly-supplied-but-blank 6th argument (e.g. a reference to an empty cell) the
/// same as a genuinely omitted one, substituting the omitted-argument default (0.1) instead of
/// letting it flow through normal blank-to-0 numeric coercion.
///
/// R56-io-table-listobject-5-2: the "#HEADERS" structured-reference selector
/// (StructuredReferenceResolver.cs) had no HeaderRowCount guard, unlike the adjacent "#TOTALS"
/// (which correctly guards on TotalsRowShown), so a headerless table's "#HEADERS" selector
/// resolved to the first DATA row instead of failing to resolve.
/// </summary>
public sealed class R56_FormulaBucketFixesTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    // --- R56-formula-xlookup-modes-5-1 ---

    [Fact]
    public void Xlookup_ArrayLookupValue_OneByOneSearchModeRange_Broadcasts()
    {
        // A1:A3 = {"A","B","C"}, B1:B3 = {1,2,3}, D1:D2 = {"B","C"} (array lookup values),
        // E1 = -1 (a valid search_mode) referenced as the explicit 1x1 RANGE E1:E1, not the bare
        // cell E1. Excel broadcasts the single search_mode value across both lookups, returning
        // {2;3} -- FreeX used to reject this outright with #VALUE! because the shape check
        // required an EXACT match to lookupValues' shape (2 rows), with no 1x1-broadcast escape.
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (2, 1, new TextValue("B")), (3, 1, new TextValue("C")),
            (1, 2, new NumberValue(1)), (2, 2, new NumberValue(2)), (3, 2, new NumberValue(3)),
            (1, 4, new TextValue("B")), (2, 4, new TextValue("C")),
            (1, 5, new NumberValue(-1)));

        var result = _eval.Evaluate("=XLOOKUP(D1:D2,A1:A3,B1:B3,,,E1:E1)", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(1);
        result.At(1, 1).Should().Be(new NumberValue(2));
        result.At(2, 1).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Xlookup_ArrayLookupValue_MismatchedNonOneByOneModeRange_StillErrors()
    {
        // Sibling no-regression: a match_mode/search_mode RANGE that is neither shaped like
        // lookupValues NOR a genuine 1x1 must still be rejected with #VALUE! -- the broadcast
        // exception must not swallow genuine shape mismatches. Here D1:D2 is 2 rows but the
        // search_mode range E1:E3 is 3 rows -- neither equal nor 1x1.
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (2, 1, new TextValue("B")), (3, 1, new TextValue("C")),
            (1, 2, new NumberValue(1)), (2, 2, new NumberValue(2)), (3, 2, new NumberValue(3)),
            (1, 4, new TextValue("B")), (2, 4, new TextValue("C")),
            (1, 5, new NumberValue(-1)), (2, 5, new NumberValue(-1)), (3, 5, new NumberValue(-1)));

        _eval.Evaluate("=XLOOKUP(D1:D2,A1:A3,B1:B3,,,E1:E3)", sheet).Should().Be(ErrorValue.Value);
    }

    // --- R56-formula-information-fns-5-1 ---

    [Fact]
    public void Isformula_IndexReferenceArgument_RecognizesUnderlyingFormulaCell()
    {
        // A2 holds a formula ("=1+1"); INDEX(A1:A3,2) resolves to A2 as a genuine reference, so
        // ISFORMULA(INDEX(A1:A3,2)) must be TRUE, exactly like ISREF(INDEX(A1:A3,2)) already is.
        var sheet = MakeSheet((1, 1, new NumberValue(5)), (3, 1, new NumberValue(7)));
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 1), "1+1");

        _eval.Evaluate("=ISFORMULA(INDEX(A1:A3,2))", sheet).Should().Be(new BoolValue(true));
        _eval.Evaluate("=FORMULATEXT(INDEX(A1:A3,2))", sheet).Should().Be(new TextValue("=1+1"));
    }

    [Fact]
    public void Isformula_PlainCellArgument_StillWorksNormally()
    {
        // Sibling no-regression: the ordinary plain-cell-reference path (no INDEX/CHOOSE wrapper)
        // must be unaffected by widening the FunctionCallNode branch.
        var sheet = MakeSheet((1, 1, new NumberValue(5)));
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "2+3");

        _eval.Evaluate("=ISFORMULA(A1)", sheet).Should().Be(new BoolValue(true));
        _eval.Evaluate("=FORMULATEXT(A1)", sheet).Should().Be(new TextValue("=2+3"));
    }

    // --- R56-formula-legacy-array-cse-5-1 ---

    [Fact]
    public void BareNamedRange_InCurrentCellContext_ImplicitlyIntersectsToCurrentRow()
    {
        // Name "Flags" = B1:B10. A formula in row 5 (=Flags) must read B5, not the whole range's
        // top-left cell B1 -- mirroring EvaluateRange's implicit-intersection behaviour for a bare
        // cell-range reference ("=B1:B10" in row 5 already correctly reads B5).
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new BoolValue(false));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new BoolValue(true));
        workbook.DefineNamedRange("Flags", new GridRange(
            new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 10, 2)));

        _eval.Evaluate("=Flags", sheet, workbook, currentCell: new CellAddress(sheet.Id, 5, 1))
            .Should().Be(new BoolValue(true));
    }

    [Fact]
    public void NamedRange_AsDirectFunctionArgument_StillReturnsFullRange()
    {
        // Sibling no-regression: SUM(Flags) is a direct range ARGUMENT to a function, which must
        // still sum the FULL named range, not the row-intersected scalar -- proving the implicit
        // intersection fix is scoped to the bare-reference path only (EvaluateNamedRange), not the
        // NamedRangeNode-as-function-argument fast path.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(3));
        workbook.DefineNamedRange("Flags", new GridRange(
            new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 3, 2)));

        _eval.Evaluate("=SUM(Flags)", sheet, workbook, currentCell: new CellAddress(sheet.Id, 2, 1))
            .Should().Be(new NumberValue(6));
    }

    // --- R56-formula-financial-tvm-5-1 ---

    [Fact]
    public void Rate_ExplicitBlankGuessReference_CoercesToZeroNotDefault()
    {
        // A1 is a genuinely blank cell, explicitly referenced (not omitted) as RATE's 6th
        // (guess) argument. Excel coerces a blank-cell numeric argument to 0, seeding the Newton
        // solve differently than the omitted-argument default of 0.1 -- for this cash flow the two
        // seeds converge to different (both legitimate) roots, so the results must differ.
        var sheet = MakeSheet();

        var withExplicitBlankGuess = _eval.Evaluate("=RATE(360,-700,100000,0,0,A1)", sheet);
        var withOmittedGuess = _eval.Evaluate("=RATE(360,-700,100000)", sheet);

        withExplicitBlankGuess.Should().BeOfType<NumberValue>();
        withOmittedGuess.Should().BeOfType<NumberValue>();
        ((NumberValue)withExplicitBlankGuess).Value.Should().NotBe(((NumberValue)withOmittedGuess).Value);
        // Guess = 0 converges to the (guess-dependent) root Excel itself finds for this cash flow.
        ((NumberValue)withExplicitBlankGuess).Value.Should().BeApproximately(-1.9844261678552, 1e-6);
    }

    [Fact]
    public void Rate_OmittedGuess_StillDefaultsToPointOne()
    {
        // Sibling no-regression: a genuinely omitted 6th argument (fewer than 6 arguments written)
        // must still default to Excel's 0.1 guess, converging to the economically sensible root.
        var sheet = MakeSheet();

        var result = _eval.Evaluate("=RATE(360,-700,100000,0,0)", sheet);

        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(0.00625955727397086, 1e-9);
    }

    // --- R56-io-table-listobject-5-2 ---

    [Fact]
    public void HeaderlessTable_HeadersSelector_DoesNotResolveToFirstDataRow()
    {
        // Table1 spans A1:B3 with HeaderRowCount = 0 (a headerless table) -- A1:B1 is the FIRST
        // DATA row (10,20), not a header row. Table1[#Headers] must not silently resolve to that
        // data row; real Excel has no header row to reference here.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(40));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(50));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(60));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            HeaderRowCount = 0,
            TotalsRowShown = false,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Col1"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Col2"));
        sheet.StructuredTables.Add(table);

        var result = _eval.Evaluate("=SUM(Table1[#Headers])", sheet, workbook);

        result.Should().NotBe(new NumberValue(30));
        result.Should().BeOfType<ErrorValue>();
    }

    [Fact]
    public void NormalTable_HeadersSelector_StillResolvesToHeaderRow()
    {
        // Sibling no-regression: an ordinary table WITH a header row must still resolve
        // Table1[#Headers] to that header row exactly as before.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Col1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Col2"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(40));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            HeaderRowCount = 1,
            TotalsRowShown = false,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Col1"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Col2"));
        sheet.StructuredTables.Add(table);

        _eval.Evaluate("=COLUMNS(Table1[#Headers])", sheet, workbook).Should().Be(new NumberValue(2));
    }
}
