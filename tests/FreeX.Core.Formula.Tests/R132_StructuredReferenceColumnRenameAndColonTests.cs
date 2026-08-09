using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

// R132-fmlstructuredref-columnrename-family / R132-fmlstructuredref-columncolon-sectionqualified:
//
// (a) HIGH: renaming a table column header via an ORDINARY cell edit (there is no dedicated
//     "rename column" command -- see R50-io-table-totals-calc-3-1) silently broke every OTHER
//     formula in the workbook that still literally named the OLD column text. The resolver's
//     column lookup (FindColumnIndex) only ever matched the table's LIVE header-cell text, so a
//     formula written before the rename -- on any sheet, or inside a defined/named formula (which
//     shares this same evaluation pipeline) -- lost its match the instant the header was retyped,
//     producing #NAME? with no indication anything had changed.
//
// (b) MED: a section-qualified structured reference whose column name literally contains a colon
//     (e.g. Table1[[#Data],[Q1:Q2]], where "Q1:Q2" is ONE column name, bracket-escaped) was always
//     mis-parsed as a two-column range from "Q1" to "Q2" -- because the combined-selector parser
//     stripped every bracket from the whole selector before splitting on ',' and ':', destroying
//     the distinction between one bracket-wrapped escape group and two separate bracket groups
//     joined by ':'. The equivalent BARE (unqualified) selector already parsed correctly
//     (R26_StructuredRefColonColumnNameTests) -- only the #section-qualified form was broken.
public sealed class R132_StructuredReferenceColumnRenameAndColonTests
{
    // ── (a) HIGH: column rename via ordinary header-cell edit ──────────────────────────────

    [Fact]
    public void HeaderRename_FormulaOnOtherSheetReferencingOldColumnName_StillResolves()
    {
        var (workbook, dataSheet, otherSheet) = BuildRenameFixture();

        // An ordinary cell edit to the header cell -- no RenameStructuredTableColumn command
        // exists, so StructuredTableColumnModel.Name is left stale at "Sales" while the sheet
        // cell (what the user actually sees) now reads "Revenue".
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 2), new TextValue("Revenue"));

        var evaluator = new FormulaEvaluator();

        // A formula on a DIFFERENT sheet, still literally naming the OLD column text, must keep
        // resolving after the rename -- exactly like Excel, which rewrites the formula text
        // workbook-wide the instant the header changes. Before the fix this returned #NAME?.
        var crossSheetResult = evaluator.Evaluate("=SUM(SalesTable[Sales])", otherSheet, workbook);
        crossSheetResult.Should().Be(new NumberValue(30));

        // Same guarantee inside a workbook-global named formula (a defined name) -- named
        // formulas share this exact evaluation pipeline, so the fix must cover them too.
        workbook.NamedFormulas["OldNameTotal"] = "SUM(SalesTable[Sales])";
        var namedFormulaResult = evaluator.Evaluate("=OldNameTotal", otherSheet, workbook);
        namedFormulaResult.Should().Be(new NumberValue(30));
    }

    [Fact]
    public void HeaderRename_FormulaUsingNewColumnName_StillResolves_NoRegression()
    {
        // Sibling: the already-working live-header-text match (R50-io-table-totals-calc-3-1) must
        // be completely unaffected by the stale-name fallback -- a formula using the NEW column
        // text still resolves via the primary (live-text) match path, not the fallback.
        var (workbook, dataSheet, otherSheet) = BuildRenameFixture();
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 2), new TextValue("Revenue"));

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=SUM(SalesTable[Revenue])", otherSheet, workbook);

        result.Should().Be(new NumberValue(30));
    }

    [Fact]
    public void HeaderRename_UnrelatedNonexistentColumnName_StillFails_GuardNotWidened()
    {
        // Guard: the fallback only aliases a column's OWN stale stored name -- it must not turn
        // into a wildcard that resolves ANY unmatched selector once some column in the table has
        // been renamed. A selector that was never any column's name (live or stored) must keep
        // failing (#NAME?) exactly as before.
        var (workbook, dataSheet, otherSheet) = BuildRenameFixture();
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 2), new TextValue("Revenue"));

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=SUM(SalesTable[NoSuchColumn])", otherSheet, workbook);

        result.Should().BeOfType<ErrorValue>();
        ((ErrorValue)result).Code.Should().Be("#NAME?");
    }

    private static (Workbook Workbook, Sheet DataSheet, Sheet OtherSheet) BuildRenameFixture()
    {
        var workbook = new Workbook("Test");
        var dataSheet = workbook.AddSheet("Data");
        var otherSheet = workbook.AddSheet("Report");

        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 1), new TextValue("Region"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 2), new TextValue("Sales"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 2, 1), new TextValue("North"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 2, 2), new NumberValue(10));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 3, 1), new TextValue("South"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 3, 2), new NumberValue(20));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "SalesTable",
            DisplayName = "SalesTable",
            Range = new GridRange(new CellAddress(dataSheet.Id, 1, 1), new CellAddress(dataSheet.Id, 3, 2))
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Sales"));
        dataSheet.StructuredTables.Add(table);

        return (workbook, dataSheet, otherSheet);
    }

    // ── (b) MED: section-qualified colon-in-column-name selector ───────────────────────────

    [Fact]
    public void SectionQualifiedColonColumnName_ResolvesLiteralColumnNotRange()
    {
        var (workbook, sheet) = BuildColonColumnFixture();
        var evaluator = new FormulaEvaluator();

        // Table1[[#Data],[Q1:Q2]] must resolve to the single literal column "Q1:Q2" (2+4=6), not
        // mis-parse as the range from column "Q1" through column "Q2" (which would sum
        // (1+3)+(2+4)=10 -- the value the pre-fix bug actually produced).
        var result = evaluator.Evaluate("=SUM(Table1[[#Data],[Q1:Q2]])", sheet, workbook);

        result.Should().Be(new NumberValue(6));
    }

    [Fact]
    public void SectionQualifiedGenuineColumnRange_StillResolvesAsRange_NoRegression()
    {
        // Sibling: the equivalent GENUINE two-bracket-group range within a #section-qualified
        // selector must still resolve as a range, unaffected by the single-bracket-group guard.
        var (workbook, sheet) = BuildColonColumnFixture();
        var evaluator = new FormulaEvaluator();

        var result = evaluator.Evaluate("=SUM(Table1[[#Data],[Q1]:[Q2]])", sheet, workbook);

        result.Should().Be(new NumberValue(10));
    }

    [Fact]
    public void SectionQualifiedSingleColumn_NoColon_StillResolves_NoRegression()
    {
        // Sibling: an ordinary #section-qualified SINGLE column selector (no colon at all) must
        // still resolve via the single-column combined-selector path, unaffected by the new
        // single-bracket-group short-circuit in the range parser.
        var (workbook, sheet) = BuildColonColumnFixture();
        var evaluator = new FormulaEvaluator();

        var result = evaluator.Evaluate("=SUM(Table1[[#Data],[Q1]])", sheet, workbook);

        result.Should().Be(new NumberValue(4));
    }

    private static (Workbook Workbook, Sheet Sheet) BuildColonColumnFixture()
    {
        var workbook = new Workbook("StructuredRefColonSectionTest");
        var sheet = workbook.AddSheet("Data");

        // Columns: A="Q1", B="Q2", C="Q1:Q2" (a literal, perfectly legal Excel header), D="Total"
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Q2"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Q1:Q2"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Total"));

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new NumberValue(5));

        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new NumberValue(11));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 4)),
            HasAutoFilter = true,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Q1"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Q2"));
        table.Columns.Add(new StructuredTableColumnModel(3, "Q1:Q2"));
        table.Columns.Add(new StructuredTableColumnModel(4, "Total"));
        sheet.StructuredTables.Add(table);

        return (workbook, sheet);
    }
}
