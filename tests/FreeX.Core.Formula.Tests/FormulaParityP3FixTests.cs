using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Tests for the P3 formula-parity cluster fixes:
///   1. IF/IFS text "TRUE"/"FALSE" condition coercion
///   2. Blank COUNTIF/SUMIF criteria matches 0, not blanks
///   3. GETPIVOTDATA OrdinalIgnoreCase field/item matching
///   4. Named range full-column/row clamp in fast aggregates
/// </summary>
public sealed class FormulaParityP3FixTests
{
    private readonly FormulaEvaluator _eval = new();

    // ─────────────────────────────────────────────────────────────────────────
    // Fix 1: IF/IFS text "TRUE"/"FALSE" coercion
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("=IF(\"TRUE\",1,2)", 1)]
    [InlineData("=IF(\"FALSE\",1,2)", 2)]
    [InlineData("=IF(\"true\",1,2)", 1)]      // case-insensitive
    [InlineData("=IF(\"false\",1,2)", 2)]
    [InlineData("=IF(\"True\",1,2)", 1)]
    [InlineData("=IF(\"False\",1,2)", 2)]
    public void If_TextTrueFalse_CoercesToBoolean(string formula, double expected)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _eval.Evaluate(formula, sheet).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=IF(\"yes\",1,2)")]
    [InlineData("=IF(\"1\",1,2)")]    // numeric text doesn't coerce in IF condition
    [InlineData("=IF(\"no\",1,2)")]
    public void If_NonBoolText_ReturnsValueError(string formula)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _eval.Evaluate(formula, sheet).Should().Be(ErrorValue.Value);
    }

    [Theory]
    [InlineData("=IFS(\"TRUE\",1,\"FALSE\",2)", 1)]
    [InlineData("=IFS(\"FALSE\",1,\"TRUE\",2)", 2)]
    [InlineData("=IFS(\"false\",1,\"true\",2)", 2)]
    public void Ifs_TextTrueFalse_CoercesToBoolean(string formula, double expected)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _eval.Evaluate(formula, sheet).Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void Ifs_NonBoolText_ReturnsValueError()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _eval.Evaluate("=IFS(\"yes\",1)", sheet).Should().Be(ErrorValue.Value);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fix 2: Blank COUNTIF/SUMIF criteria → match zeros, not blanks
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Countif_BlankCriteria_CountsZeros_NotBlanks()
    {
        // A1=0, A2=blank, A3=0, A4=1
        // Blank criteria (B1=empty) should match A1 and A3 (value=0), NOT A2 (blank).
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(0));
        // A2 intentionally left blank
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(0));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(1));
        // B1 intentionally left blank — used as criteria reference
        // The formula reads B1 as BlankValue criteria
        wb.DefineNamedRange("BlankCell", new GridRange(
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, 1, 2)));

        // Use COUNTIF with a reference to the blank cell B1 as criteria
        _eval.Evaluate("=COUNTIF(A1:A4,B1)", sheet, wb).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Countif_EmptyStringCriteria_StillCountsBlanks()
    {
        // "" (explicit empty string) should still match blank cells — this is unchanged.
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(0));
        // A2 blank
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(0));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(1));

        // "" matches blank cells: A2 is blank → count = 1
        _eval.Evaluate("=COUNTIF(A1:A4,\"\")", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Sumif_BlankCriteria_SumsRowsWhereCriteriaColumnIsZero()
    {
        // A: 0, blank, 0, 1 — B: 10, 20, 30, 40
        // Blank criteria → sums where A=0: B1+B3=40
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(0));
        // A2 blank
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(0));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(40));

        // SUMIF(A1:A4, C1, B1:B4) where C1 is blank → criteria = 0
        _eval.Evaluate("=SUMIF(A1:A4,C1,B1:B4)", sheet, wb).Should().Be(new NumberValue(40));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fix 3: GETPIVOTDATA OrdinalIgnoreCase field/item matching
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetPivotData_MixedCaseFieldAndItem_ReturnsValue()
    {
        // Verifies that OrdinalIgnoreCase is used for all field and item comparisons.
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 6), new TextValue("Sum of Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 5), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 6), new NumberValue(25));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 5), new TextValue("Grand Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 6), new NumberValue(25));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 4, 6))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        // Mixed-case field name and item value — OrdinalIgnoreCase should accept all variants
        _eval.Evaluate("=GETPIVOTDATA(\"SUM OF AMOUNT\",E2,\"REGION\",\"east\")", sheet, wb)
            .Should().Be(new NumberValue(25));
        _eval.Evaluate("=GETPIVOTDATA(\"sum of amount\",E2,\"Region\",\"EAST\")", sheet, wb)
            .Should().Be(new NumberValue(25));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fix 4: Named range full-column/row clamp in fast aggregates
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Sum_NamedFullColumnRange_EqualsDirectColumnSum()
    {
        // Data defined as a named range over $A:$B (full columns) should give the same
        // result as =SUM(A:B), not #REF!
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5));

        // Define "Data" as full columns A:B
        wb.DefineNamedRange("Data", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 2)));

        var namedResult = _eval.Evaluate("=SUM(Data)", sheet, wb);
        var directResult = _eval.Evaluate("=SUM(A:B)", sheet, wb);

        namedResult.Should().Be(directResult);
        namedResult.Should().Be(new NumberValue(35));
    }

    [Fact]
    public void Sum_NamedFullRowRange_EqualsDirectRowSum()
    {
        // Named range over full rows 1:2 should match =SUM(1:2)
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(4));

        wb.DefineNamedRange("RowData", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, CellAddress.MaxCol)));

        var namedResult = _eval.Evaluate("=SUM(RowData)", sheet, wb);
        var directResult = _eval.Evaluate("=SUM(1:2)", sheet, wb);

        namedResult.Should().Be(directResult);
        namedResult.Should().Be(new NumberValue(14));
    }

    [Fact]
    public void Sum_NamedFullColumnRange_EmptySheet_ReturnsZero()
    {
        // Named full-column range on an empty sheet → 0, not #REF!
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        wb.DefineNamedRange("Empty", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1)));

        _eval.Evaluate("=SUM(Empty)", sheet, wb).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Sum_NamedFullColumnRange_SheetQualified_Works()
    {
        // Sheet-qualified named range pointing to full columns
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(200));

        wb.DefineNamedRange("Cols", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 2)));

        _eval.Evaluate("=SUM(Cols)", sheet, wb).Should().Be(new NumberValue(300));
    }
}
