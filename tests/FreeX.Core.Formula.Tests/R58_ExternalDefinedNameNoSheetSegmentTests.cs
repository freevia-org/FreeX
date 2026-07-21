using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R58-io-external-links-6-1: Lexer.TryReadExternalSheetQualifier required an identifier (sheet
/// name) between ']' and '!', so the external-workbook DEFINED-NAME reference form with NO sheet
/// segment at all -- e.g. <c>[1]!TaxRate</c>, Excel's real on-disk numeric-index shape for a
/// workbook-scoped name exposed by an external link (ECMA-376 SpreadsheetML formula grammar) --
/// fell through to ReadStructuredReferenceSelector(), producing a bogus
/// StructuredReferenceSelector("1") token. The parser then choked on the orphan "!TaxRate" that
/// followed, so the WHOLE formula (e.g. <c>=[1]!TaxRate+B2</c>) threw FormulaParseException --
/// silently losing even the local "+B2" half, because RecalcEngine's
/// IsLikelyExternalWorkbookReferenceFormula guard still matched the "[1]!" text and swallowed the
/// parse failure by preserving the cell's stale cached value forever.
///
/// The fix teaches the lexer to also accept the zero-length-sheet-segment shape, the parser to
/// build a NamedRangeNode carrying the opaque "[n]!Name" text (Parser.ParseExternalDefinedNameReference),
/// and ExternalSheetReferenceResolver.TryResolveExternalDefinedName (consulted from
/// SheetEvalContext.TryGetNamedFormulaText) to resolve that name against the external link's
/// cached ExternalLinkModel.DefinedNames RefersTo text by rewriting it to the already-supported
/// quoted external-sheet cell-reference form (e.g. <c>'[1]Sheet1'!$B$2</c>), so the whole formula
/// parses and its local half recomputes live.
/// </summary>
public sealed class R58_ExternalDefinedNameNoSheetSegmentTests
{
    private static Workbook BuildWorkbookWithExternalDefinedName(out Sheet sheet)
    {
        var workbook = new Workbook("Test");
        sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(50)); // local B2 = 50

        var link = new ExternalLinkModel
        {
            PackagePart = "xl/externalLinks/externalLink1.xml",
            TargetUri = "Book1.xlsx",
            TargetMode = "External",
        };
        link.SheetNames.Add("Sheet1");
        link.DefinedNames.Add(new ExternalDefinedNameModel
        {
            Name = "TaxRate",
            RefersTo = "Sheet1!$B$2", // workbook-scoped (SheetId left null)
        });

        var cachedSheet = new ExternalCachedSheetModel { SheetId = 0 };
        cachedSheet.Values[(2u, 2u)] = new NumberValue(100); // external Sheet1!B2 cached = 100
        link.CachedSheetData.Add(cachedSheet);
        workbook.ExternalLinks.Add(link);

        return workbook;
    }

    [Fact]
    public void Tokenizes_ExternalDefinedNameNoSheetSegment_MixedWithLocalCellRef()
    {
        // Before the fix this threw FormulaParseException while tokenizing/parsing; the lexer
        // itself produced a bogus StructuredReferenceSelector("1") instead of the tokens below.
        var tokens = new Lexer("=[1]!TaxRate+B2").Tokenize();

        tokens[0].Type.Should().Be(TokenType.SheetQualifier);
        tokens[0].Value.Should().Be("[1]");
        tokens[1].Type.Should().Be(TokenType.NamedRange);
        tokens[1].Value.Should().Be("TAXRATE");
        tokens[2].Type.Should().Be(TokenType.Plus);
        tokens[3].Type.Should().Be(TokenType.CellRef);
        tokens[3].Value.Should().Be("B2");
    }

    [Fact]
    public void Evaluate_ExternalDefinedNameNoSheetSegment_MixedWithLocalCell_RecalculatesLocalHalf()
    {
        // End-to-end mirror of the finding's scenario: C1 = [1]!TaxRate + B2, where TaxRate
        // resolves (via the external link's cached defined-name RefersTo + sheetDataSet) to 100,
        // and B2 is a normal local cell = 50, giving 150. Real Excel live-recalculates the local
        // half when B2 changes; before the fix the whole formula never even parsed, so it could
        // never recompute at all (RecalcEngine just preserved the prior value forever).
        var workbook = BuildWorkbookWithExternalDefinedName(out var sheet);

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=[1]!TaxRate+B2", sheet, workbook);
        result.Should().Be(new NumberValue(150));

        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(60));
        var updated = evaluator.Evaluate("=[1]!TaxRate+B2", sheet, workbook);
        updated.Should().Be(new NumberValue(160));
    }

    [Fact]
    public void Tokenizes_SheetQualifiedExternalReference_NotRegressed()
    {
        // Sibling regression guard: the already-shipped [n]SheetName!Ref sheet-qualified form
        // (R48) must still lex to a single SheetQualifier token carrying the sheet name, unaffected
        // by the new zero-length-sheet-segment lookahead.
        var tokens = new Lexer("=[1]Sheet1!A1+B2").Tokenize();

        tokens[0].Type.Should().Be(TokenType.SheetQualifier);
        tokens[0].Value.Should().Be("[1]Sheet1");
        tokens[1].Type.Should().Be(TokenType.CellRef);
        tokens[1].Value.Should().Be("A1");
    }

    [Fact]
    public void Tokenizes_StructuredTableColumnReference_NotRegressed()
    {
        // Sibling regression guard: an ordinary structured-table-reference selector (whose bracket
        // content is a column name, not all digits) must still lex as StructuredReferenceSelector.
        var tokens = new Lexer("=SUM(Table1[Column1])").Tokenize();

        tokens.Select(t => $"{t.Type}:{t.Value}")
            .Should().Contain("StructuredReferenceSelector:Column1");
    }

    [Fact]
    public void Tokenizes_FormulaWithBracketButNoTrailingBang_FallsThroughToStructuredReference()
    {
        // A digits-only bracket that is NOT followed by '!' at all (here, followed directly by
        // ')') must still fall through to the ordinary structured-reference path unchanged.
        var tokens = new Lexer("=SUM(A1:A2,[1])").Tokenize();

        tokens.Select(t => $"{t.Type}:{t.Value}")
            .Should().Contain("StructuredReferenceSelector:1");
    }
}
