using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-135 finding: the '@' this-row shorthand — both the table-qualified Table1[@] and the bare
/// unqualified [@] (no column name at all, meaning "this entire row", equivalent to the long-form
/// Table1[#This Row] / [#This Row]) — always evaluated to #NAME?.
///
/// Two independent parse/evaluate gaps combined to cause this:
///
///  - Parser.ParsePrimary's bare TokenType.StructuredReferenceSelector case (no table qualifier)
///    only recognized the '@' shorthand when `value.Length > 1` (i.e. an actual column name follows
///    the '@', like [@Amount]). The single-character selector "@" fell through to the generic
///    unqualified-column branch, which built a StructuredReferenceNode("", "@") that then hunted a
///    live table column literally named "@" — never found, so #NAME?. Fixed by dropping the
///    Length > 1 restriction and using an empty ColumnName when nothing follows '@'.
///
///  - Even where the parser already produced an empty ColumnName (the table-qualified Table1[@]
///    case already did, via `selector.Value.Trim()[1..].Trim()` on "@"),
///    FormulaEvaluator.References.EvaluateCurrentRowReference unconditionally routed every
///    StructuredCurrentRowReferenceNode through StructuredReferenceResolver.ResolveCurrentRowColumn
///    (a single-column lookup) and its column-RANGE fallback — both of which need an actual column
///    name/range text to search for, so an empty ColumnName always missed and yielded #NAME? too.
///    Fixed by special-casing an empty/whitespace ColumnName up front and routing it through
///    StructuredReferenceResolver.Resolve(..., "#This Row", ...) — the same whole-row resolution the
///    long form already uses successfully.
///
/// The related shorthands ([@Column], [@[Column Name]]) and the long form (#This Row) are covered by
/// sibling no-regression tests below (and were already covered pre-existing by
/// R65_FmlStructuredCurrentRowRangeTests / StructuredReferenceCurrentRowTests). A separate sibling
/// test confirms the '@' implicit-intersection operator elsewhere in a formula (e.g. =@A1:A3, unrelated
/// lexer token) still parses/evaluates unchanged, since this fix only touches the
/// TokenType.StructuredReferenceSelector parse arms.
/// </summary>
public sealed class R135_FmlStructuredAtShorthandThisRowTests
{
    private static Sheet BuildQ1Q2TotalTable(out Workbook workbook)
    {
        workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Q2"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(200));
        // Column 3 ("Total") is deliberately left blank in the sheet -- these tests pass the
        // formula text directly to FormulaEvaluator.Evaluate with a currentCell context rather than
        // storing the formula via sheet.SetFormula, so referencing the whole row (which necessarily
        // includes the Total column itself) reads that blank cell as 0 rather than recursing into a
        // real self-referential formula evaluation / circular reference.

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3))
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Q1"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Q2"));
        table.Columns.Add(new StructuredTableColumnModel(3, "Total"));
        sheet.StructuredTables.Add(table);
        return sheet;
    }

    // ── The bug: both spellings of the this-row shorthand always returned #NAME? ──

    [Fact]
    public void TableQualifiedAtShorthand_EvaluatesWholeCurrentRow_InsteadOfNameError()
    {
        var sheet = BuildQ1Q2TotalTable(out var workbook);
        var evaluator = new FormulaEvaluator();

        // Formula cell sits in the Total column (row 2), which is within Table1's own column span --
        // the requirement the resolver's "this row" path already enforces for the long form.
        var currentCell = new CellAddress(sheet.Id, 2, 3);
        var result = evaluator.Evaluate("=SUM(Table1[@])", sheet, workbook, currentCell);

        // Q1 (10) + Q2 (20) + Total (blank -> 0) = 30, matching the equivalent long-form
        // Table1[#This Row] sum below.
        result.Should().Be(new NumberValue(30));

        var currentCellRow3 = new CellAddress(sheet.Id, 3, 3);
        var resultRow3 = evaluator.Evaluate("=SUM(Table1[@])", sheet, workbook, currentCellRow3);
        resultRow3.Should().Be(new NumberValue(300));
    }

    [Fact]
    public void BareUnqualifiedAtShorthand_EvaluatesWholeCurrentRow_InsteadOfNameError()
    {
        var sheet = BuildQ1Q2TotalTable(out var workbook);
        var evaluator = new FormulaEvaluator();

        var currentCell = new CellAddress(sheet.Id, 2, 3);
        var result = evaluator.Evaluate("=SUM([@])", sheet, workbook, currentCell);

        result.Should().Be(new NumberValue(30));
    }

    // ── Sibling no-regression: the long form must still resolve identically ──

    [Fact]
    public void TableQualifiedThisRowLongForm_StillEvaluatesWholeCurrentRow_NoRegression()
    {
        var sheet = BuildQ1Q2TotalTable(out var workbook);
        var evaluator = new FormulaEvaluator();

        var currentCell = new CellAddress(sheet.Id, 2, 3);
        var result = evaluator.Evaluate("=SUM(Table1[#This Row])", sheet, workbook, currentCell);

        result.Should().Be(new NumberValue(30));
    }

    // ── Sibling no-regression: the related [@Column] / [@[Column Name]] shorthands still work ──

    [Fact]
    public void SingleColumnAtShorthand_StillResolves_NoRegression()
    {
        var sheet = BuildQ1Q2TotalTable(out var workbook);
        var evaluator = new FormulaEvaluator();

        var currentCell = new CellAddress(sheet.Id, 2, 3);
        var result = evaluator.Evaluate("=Table1[@Q1]", sheet, workbook, currentCell);

        result.Should().Be(new NumberValue(10));
    }

    [Fact]
    public void BracketedColumnNameAtShorthand_StillResolves_NoRegression()
    {
        var workbook = new Workbook("Test2");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Sales Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(42));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2))
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Sales Amount"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Total"));
        sheet.StructuredTables.Add(table);

        var evaluator = new FormulaEvaluator();
        var currentCell = new CellAddress(sheet.Id, 2, 2);
        var result = evaluator.Evaluate("=Table1[@[Sales Amount]]", sheet, workbook, currentCell);

        result.Should().Be(new NumberValue(42));
    }

    // ── Sibling no-regression: a literal '@' elsewhere in a formula (implicit intersection
    // operator, an entirely different lexer token) must still parse/evaluate unchanged -- this fix
    // only touches the TokenType.StructuredReferenceSelector parse arms, never the '@' operator. ──

    [Fact]
    public void AtIntersectionOperatorElsewhereInFormula_StillEvaluatesUnchanged_NoRegression()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(30));

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate(
            "=@A2:A4", sheet, currentCell: new CellAddress(sheet.Id, 3, 2));

        // The formula cell sits on row 3, so implicit intersection against A2:A4 picks A3 = 20.
        result.Should().Be(new NumberValue(20));
    }
}
