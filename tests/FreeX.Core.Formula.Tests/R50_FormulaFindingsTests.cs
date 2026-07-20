using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-50 formula-bucket fixes:
///  - R50-meta-2: a sheet-qualified reference from within a named formula to a DIFFERENT
///    same-named sheet-scoped named formula must not false-positive as circular.
///  - R50-formula-financial-loan-3-1: IPMT/PPMT/CUMIPMT/CUMPRINC must validate the raw `type`
///    argument (0 or 1 only) BEFORE truncating it, matching Excel's #NUM! for e.g. type=1.5.
///  - R50-formula-financial-loan-3-2: RRI must permit an opposite-signed pv/fv at nper=1.
///  - R50-formula-dynamic-filter-unique-3-1: UNIQUE's multi-column dedup key must treat -0 and 0
///    as equal, matching Excel (and the already-correct single-column comparer path).
///  - R50-formula-pivot-getpivotdata-3-1: GETPIVOTDATA field/item/data_field arguments passed as
///    bare cell references must resolve to the cell's value, not stringify the wrapping RangeValue.
///  - R50-formula-text-currency-numsys-3-1: DOLLARDE/DOLLARFR must not clamp the digit count to a
///    minimum of 1 — fraction=1 is a legitimate identity case.
///  - R50-io-table-totals-calc-3-1: an ordinary edit to a table's header-row cell text must be
///    honoured by structured-reference resolution (read the live header cell, not the possibly
///    stale StructuredTableColumnModel.Name) — see StructuredReferenceResolver.ColumnHeaderText.
/// </summary>
public sealed class R50_FormulaFindingsTests
{
    private readonly FormulaEvaluator _eval = new();
    private readonly Sheet _sheet = new(SheetId.New(), "Sheet1");

    private static double Num(ScalarValue v) => ((NumberValue)v).Value;

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    // ── R50-meta-2: sheet-scoped same-named formulas must key cycle detection by scope ──

    [Fact]
    public void SheetQualifiedReferenceToDistinctSameNamedScopedFormula_DoesNotFalsePositiveAsCircular()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        // Two DIFFERENT formulas that both happen to be named "Foo", each scoped to its own sheet.
        workbook.DefineNamedFormula("Foo", "Sheet2!Foo+1", sheet1.Id);
        workbook.DefineNamedFormula("Foo", "1", sheet2.Id);

        var result = _eval.Evaluate("=Foo", sheet1, workbook);

        result.Should().Be(new NumberValue(2));
    }

    [Fact]
    public void SheetQualifiedReferenceForming_GenuineCycleAcrossTwoScopedFormulas_StillReturnsRef_NoRegression()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        // A genuine cycle: Sheet1!Foo -> Sheet2!Foo -> Sheet1!Foo.
        workbook.DefineNamedFormula("Foo", "Sheet2!Foo+1", sheet1.Id);
        workbook.DefineNamedFormula("Foo", "Sheet1!Foo+1", sheet2.Id);

        var result = _eval.Evaluate("=Foo", sheet1, workbook);

        result.Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void GlobalNamedFormula_DirectSelfReference_StillReturnsRef_NoRegression()
    {
        // Pre-existing bare-name-cycle behaviour (NamedFormulaTests.CircularNamedFormula_*) must
        // survive the switch to scope-keyed cycle detection.
        var workbook = new Workbook("Test");
        workbook.NamedFormulas["Circ"] = "Circ+1";

        _eval.Evaluate("=Circ", _sheet, workbook).Should().Be(ErrorValue.Ref);
    }

    // ── R50-formula-financial-loan-3-1: type arg validated before truncation ──

    [Theory]
    [InlineData("=IPMT(0.05,1,10,1000,0,1.5)")]
    [InlineData("=PPMT(0.05,1,10,1000,0,1.5)")]
    [InlineData("=CUMIPMT(0.05,10,1000,1,5,1.5)")]
    [InlineData("=CUMPRINC(0.05,10,1000,1,5,1.5)")]
    public void LoanPaymentFunctions_NonIntegerType_ReturnsNumError(string formula)
    {
        _eval.Evaluate(formula, _sheet).Should().Be(ErrorValue.Num);
    }

    [Theory]
    [InlineData("=IPMT(0.05,1,10,1000,0,0)")]
    [InlineData("=IPMT(0.05,1,10,1000,0,1)")]
    [InlineData("=PPMT(0.05,1,10,1000,0,0)")]
    [InlineData("=PPMT(0.05,1,10,1000,0,1)")]
    [InlineData("=CUMIPMT(0.05,10,1000,1,5,0)")]
    [InlineData("=CUMIPMT(0.05,10,1000,1,5,1)")]
    [InlineData("=CUMPRINC(0.05,10,1000,1,5,0)")]
    [InlineData("=CUMPRINC(0.05,10,1000,1,5,1)")]
    public void LoanPaymentFunctions_ValidIntegerType_StillComputes_NoRegression(string formula)
    {
        _eval.Evaluate(formula, _sheet).Should().BeOfType<NumberValue>();
    }

    // ── R50-formula-financial-loan-3-2: RRI nper=1 permits opposite-signed pv/fv ──

    [Fact]
    public void Rri_Nper1_OppositeSignedPvFv_ReturnsPlainRatio_NotNumError()
    {
        // fv/pv - 1 = -50/100 - 1 = -1.5
        Num(_eval.Evaluate("=RRI(1,100,-50)", _sheet)).Should().BeApproximately(-1.5, 1e-12);
    }

    [Fact]
    public void Rri_NperGreaterThanOne_OppositeSignedPvFv_StillReturnsNumError_NoRegression()
    {
        _eval.Evaluate("=RRI(2,100,-50)", _sheet).Should().Be(ErrorValue.Num);
    }

    // ── R50-formula-dynamic-filter-unique-3-1: -0/0 treated as equal in multi-column dedup ──

    [Fact]
    public void Unique_MultiColumn_NegativeZeroAndPositiveZero_AreTreatedAsDuplicates()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(0d)), (1, 2, new TextValue("x")),
            (2, 1, new NumberValue(-0d)), (2, 2, new TextValue("x")));

        var result = _eval.Evaluate("=UNIQUE(A1:B2)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(1);
        rv.Cells[0, 1].Should().Be(new TextValue("x"));
    }

    [Fact]
    public void Unique_MultiColumn_DistinctNumbers_StillKeepsBothRows_NoRegression()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(0d)), (1, 2, new TextValue("x")),
            (2, 1, new NumberValue(1d)), (2, 2, new TextValue("x")));

        var result = _eval.Evaluate("=UNIQUE(A1:B2)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
    }

    // ── R50-formula-pivot-getpivotdata-3-1: field/item args as bare cell references ──

    [Fact]
    public void GetPivotData_FieldAndItemAsCellReferences_ResolvesToPivotValue()
    {
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 6), new TextValue("Sum of Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 5), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 6), new NumberValue(25));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 5), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 6), new NumberValue(45));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new TextValue("Grand Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 6), new NumberValue(70));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 5, 6))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        // G1/G2 hold ordinary cell-reference text — a common dashboard-driven GETPIVOTDATA pattern.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 7), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 7), new TextValue("West"));

        var result = _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,G1,G2)", sheet, wb);

        result.Should().Be(new NumberValue(45));
    }

    [Fact]
    public void GetPivotData_FieldAndItemAsStringLiterals_StillResolves_NoRegression()
    {
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 6), new TextValue("Sum of Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 5), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 6), new NumberValue(25));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 5), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 6), new NumberValue(45));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new TextValue("Grand Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 6), new NumberValue(70));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 5, 6))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var result = _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"West\")", sheet, wb);

        result.Should().Be(new NumberValue(45));
    }

    // ── R50-formula-text-currency-numsys-3-1: DOLLARDE/DOLLARFR fraction=1 identity ──

    [Fact]
    public void Dollarde_FractionOne_ReturnsIdentity()
    {
        Num(_eval.Evaluate("=DOLLARDE(1.5,1)", _sheet)).Should().Be(1.5);
    }

    [Fact]
    public void Dollarfr_FractionOne_ReturnsIdentity()
    {
        Num(_eval.Evaluate("=DOLLARFR(1.5,1)", _sheet)).Should().Be(1.5);
    }

    [Fact]
    public void Dollarde_FractionThirtyTwo_StillMatchesDocumentedExample_NoRegression()
    {
        // Microsoft's own DOLLARDE example: DOLLARDE(1.02,32) = 1.0625.
        Num(_eval.Evaluate("=DOLLARDE(1.02,32)", _sheet)).Should().BeApproximately(1.0625, 1e-9);
    }

    // ── R50-io-table-totals-calc-3-1: live header text drives structured-ref resolution ──

    [Fact]
    public void StructuredReference_AfterOrdinaryHeaderCellEdit_ResolvesToTheNewHeaderText()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "SalesTable",
            DisplayName = "SalesTable",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2))
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Sales"));
        sheet.StructuredTables.Add(table);

        // An ordinary cell edit to the header cell — no RenameStructuredTableColumn command
        // exists, so table.Columns[1].Name is left stale at "Sales" while the sheet cell (which
        // is what the user actually sees, and what they'd type in a new formula) now reads
        // "Revenue".
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Revenue"));

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=SUM(SalesTable[Revenue])", sheet, workbook);

        result.Should().Be(new NumberValue(30));
    }

    [Fact]
    public void StructuredReference_UnrenamedTable_StillResolvesByOriginalHeaderText_NoRegression()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "SalesTable",
            DisplayName = "SalesTable",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2))
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Sales"));
        sheet.StructuredTables.Add(table);

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=SUM(SalesTable[Sales])", sheet, workbook);

        result.Should().Be(new NumberValue(30));
    }
}
