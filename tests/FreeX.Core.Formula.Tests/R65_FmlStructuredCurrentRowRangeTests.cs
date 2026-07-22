using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-65 finding R65-io-table-structured-6-1 / -6-2: the '@' shorthand current-row column-RANGE
/// structured reference (Table1[@[Q1]:[Q2]]) — as opposed to the long-form
/// Table1[[#This Row],[Q1]:[Q2]] — was both mis-serialized and unresolvable at evaluation:
///
///  - FormulaSerializer.WriteNode's StructuredCurrentRowReferenceNode case doubled every ']' in
///    ColumnName unconditionally. For a plain single column that's correct escaping, but for a
///    range shorthand ColumnName holds the LITERAL bracketed range text "[Q1]:[Q2]" itself, so
///    doubling turned it into "[Q1]]:[Q2]]" (plus the arm's own closing ']'), an unterminated
///    structured reference that throws on the next re-lex. Fixed by mirroring
///    AppendStructuredReferenceSelector's own selector.StartsWith('[') guard.
///
///  - StructuredReferenceResolver.ResolveCurrentRowColumn only ever does a single-column
///    FindColumnIndex lookup, so it always failed (returned null) for the literal "[Q1]:[Q2]"
///    range text, and FormulaEvaluator.References.EvaluateCurrentRowReference had nothing else to
///    fall back to, so the whole reference evaluated to #NAME?. Fixed by adding
///    StructuredReferenceResolver.ResolveCurrentRowColumnRange (reusing
///    ResolveThisRowColumnRange / TryParseColumnRangeSelector) and having
///    EvaluateCurrentRowReference fall back to it when the single-column resolve fails.
/// </summary>
public sealed class R65_FmlStructuredCurrentRowRangeTests
{
    // The current-row resolvers require the formula's own cell to fall within the table's column
    // span (see ResolveCurrentRowColumn / ResolveCurrentRowColumnRange's Col range check) — matching
    // how [@Column] is normally used, as a calculated-column formula inside the table itself — so
    // the table includes a third "Total" column and the formula cell under test lives in it.
    private static Sheet BuildQ1Q2Table(out Workbook workbook, out StructuredTableModel table)
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

        table = new StructuredTableModel
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

    // ── R65-io-table-structured-6-1: FormulaSerializer round-trip ──

    [Fact]
    public void Serialize_CurrentRowColumnRangeShorthand_RoundTripsIntactAndReLexable()
    {
        var node = new StructuredCurrentRowReferenceNode("[Q1]:[Q2]", "Table1");

        var text = FormulaSerializer.Serialize(node);

        text.Should().Be("Table1[@[Q1]:[Q2]]");

        // Must re-parse back to an equivalent node, not throw / mis-lex.
        var reparsed = new Parser(new Lexer(text).Tokenize()).Parse();
        reparsed.Should().BeOfType<StructuredCurrentRowReferenceNode>();
        var reparsedCurrent = (StructuredCurrentRowReferenceNode)reparsed;
        // The lexer upper-cases bare NamedRange tokens, which a table name lexes as — case is not
        // semantically significant for a table name (StructuredTableNameMatches compares
        // case-insensitively), so this is expected, not a bug.
        reparsedCurrent.TableName.Should().Be("TABLE1");
        reparsedCurrent.ColumnName.Should().Be("[Q1]:[Q2]");
    }

    [Fact]
    public void Serialize_CurrentRowSingleColumnWithLiteralBracket_StillEscapesCorrectly_NoRegression()
    {
        // A plain (non-range) single-column selector containing a literal ']' still needs the
        // doubling escape so it round-trips as a literal character rather than closing early.
        var node = new StructuredCurrentRowReferenceNode("Sing]leCol", "Table1");

        var text = FormulaSerializer.Serialize(node);

        text.Should().Be("Table1[@Sing]]leCol]");
    }

    [Fact]
    public void Serialize_FormulaWithCurrentRowColumnRange_SurvivesRewriteAfterInsertRow()
    {
        // Row 5 sits ABOVE A10, so inserting a row there shifts A10 -> A11 and forces
        // FormulaRewriter.Rewrite to actually reserialize the whole AST (changed == true) —
        // exercising the same serialize path the bug corrupted, alongside the untouched
        // Table1[@[Q1]:[Q2]] structured reference.
        const string formula = "SUM(Table1[@[Q1]:[Q2]])+A10";
        var op = new InsertRowsOp("Sheet1", 5, 1);

        var rewritten = FormulaRewriter.Rewrite(formula, op, "Sheet1");

        // The lexer upper-cases the bare table-name token on reparse (case is not semantically
        // significant for a table name), so the round trip comes back as "TABLE1", not "Table1".
        rewritten.Should().Be("SUM(TABLE1[@[Q1]:[Q2]])+A11");

        // And it must still re-parse cleanly (this is what a real reserialize-on-edit would
        // otherwise corrupt per the bug description).
        var reparsed = new Parser(new Lexer(rewritten!).Tokenize()).Parse();
        reparsed.Should().NotBeNull();
    }

    // ── R65-io-table-structured-6-2: '@' shorthand column-range evaluation ──

    [Fact]
    public void CurrentRowColumnRangeShorthand_EvaluatesToSumOfCurrentRowSlice()
    {
        var sheet = BuildQ1Q2Table(out var workbook, out _);
        var evaluator = new FormulaEvaluator();

        // Current cell is row 2 (Q1=10, Q2=20) -> Q1+Q2 = 30.
        var currentCell = new CellAddress(sheet.Id, 2, 3);
        var result = evaluator.Evaluate("=SUM(Table1[@[Q1]:[Q2]])", sheet, workbook, currentCell);

        result.Should().Be(new NumberValue(30));

        // A different current row picks up that row's own slice, not a fixed one.
        var currentCellRow3 = new CellAddress(sheet.Id, 3, 3);
        var resultRow3 = evaluator.Evaluate("=SUM(Table1[@[Q1]:[Q2]])", sheet, workbook, currentCellRow3);
        resultRow3.Should().Be(new NumberValue(300));
    }

    [Fact]
    public void CurrentRowSingleColumnShorthand_StillResolves_NoRegression()
    {
        var sheet = BuildQ1Q2Table(out var workbook, out _);
        var evaluator = new FormulaEvaluator();

        var currentCell = new CellAddress(sheet.Id, 2, 3);
        var result = evaluator.Evaluate("=Table1[@[Q1]]", sheet, workbook, currentCell);

        result.Should().Be(new NumberValue(10));
    }

    [Fact]
    public void LongFormThisRowColumnRange_StillResolves_NoRegression()
    {
        var sheet = BuildQ1Q2Table(out var workbook, out _);
        var evaluator = new FormulaEvaluator();

        var currentCell = new CellAddress(sheet.Id, 2, 3);
        var result = evaluator.Evaluate("=SUM(Table1[[#This Row],[Q1]:[Q2]])", sheet, workbook, currentCell);

        result.Should().Be(new NumberValue(30));
    }
}
