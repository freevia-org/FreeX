using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R48-io-external-links-3-1: the lexer unconditionally dispatched a leading '[' to
/// ReadStructuredReferenceSelector(), so the unquoted on-disk external-workbook reference form
/// Excel actually writes -- <c>[n]SheetName!Ref</c>, e.g. <c>[1]Sheet1!A1</c> -- never parsed: it
/// lexed as a bogus StructuredReferenceSelector("1") followed by a stray SheetQualifier("Sheet1")
/// token, which the parser then rejected with "Unexpected token 'Sheet1'". Any formula mixing that
/// external reference with a local cell reference (e.g. <c>=[1]Sheet1!A1+B2</c>) therefore failed
/// to parse at all, silently losing the local B2 dependency edge (RecalcEngine only registers
/// dependencies after a successful parse).
///
/// The fix recognizes the unquoted numeric-index external-sheet-qualifier shape up front and emits
/// a single SheetQualifier token whose value matches what the already-working quoted form
/// ('[1]Sheet1'!A1) produces, so the rest of the parser/evaluator (ExternalSheetReferenceResolver)
/// handles both shapes identically without any further changes.
/// </summary>
public sealed class R48_UnquotedExternalSheetQualifierLexerTests
{
    [Fact]
    public void Tokenizes_UnquotedExternalSheetQualifier_NumericIndexForm_MixedWithLocalCellRef()
    {
        // The exact failure scenario: an external reference combined with a local cell reference
        // via '+'. Before the fix this threw FormulaParseException instead of producing tokens.
        var tokens = new Lexer("=[1]Sheet1!A1+B2").Tokenize();

        tokens[0].Type.Should().Be(TokenType.SheetQualifier);
        tokens[0].Value.Should().Be("[1]Sheet1");
        tokens[1].Type.Should().Be(TokenType.CellRef);
        tokens[1].Value.Should().Be("A1");
        tokens[2].Type.Should().Be(TokenType.Plus);
        tokens[3].Type.Should().Be(TokenType.CellRef);
        tokens[3].Value.Should().Be("B2");
    }

    [Fact]
    public void Evaluate_UnquotedExternalSheetReference_MixedWithLocalCell_RecalculatesLocalHalf()
    {
        // End-to-end mirror of the finding's scenario: C1 = [1]Sheet1!A1 + B2, where A1 is only
        // available via the external link's cached value (100) and B2 is a normal local cell. Real
        // Excel live-recalculates the local half; before the fix the whole formula failed to parse,
        // so it could never be recalculated at all (RecalcEngine just preserved the prior value).
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(50)); // B2 = 50

        var link = new ExternalLinkModel
        {
            PackagePart = "xl/externalLinks/externalLink1.xml",
            TargetUri = "Book1.xlsx",
            TargetMode = "External",
        };
        link.SheetNames.Add("Sheet1");
        var cachedSheet = new ExternalCachedSheetModel { SheetId = 0 };
        cachedSheet.Values[(1u, 1u)] = new NumberValue(100); // cached [1]Sheet1!A1 = 100
        link.CachedSheetData.Add(cachedSheet);
        workbook.ExternalLinks.Add(link);

        var evaluator = new FormulaEvaluator();
        var result = evaluator.Evaluate("=[1]Sheet1!A1+B2", sheet, workbook);

        result.Should().Be(new NumberValue(150));
    }

    [Fact]
    public void Tokenizes_StructuredTableColumnReference_NotRegressed()
    {
        // Sibling regression guard: an ordinary structured-table-reference selector (whose bracket
        // content is a column name, not all digits) must still lex as StructuredReferenceSelector,
        // completely unaffected by the new external-sheet-qualifier lookahead.
        var tokens = new Lexer("=SUM(Table1[Column1])").Tokenize();

        tokens.Select(t => $"{t.Type}:{t.Value}")
            .Should().Contain("StructuredReferenceSelector:Column1");
    }

    [Fact]
    public void Tokenizes_FormulaWithBracketButNoTrailingSheetBang_FallsThroughToStructuredReference()
    {
        // A digits-only bracket that is NOT followed by an identifier+'!' (here, followed directly
        // by ')') must still fall through to the ordinary structured-reference path unchanged,
        // rather than being misrecognized as an external-sheet qualifier.
        var tokens = new Lexer("=SUM(A1:A2,[1])").Tokenize();

        tokens.Select(t => $"{t.Type}:{t.Value}")
            .Should().Contain("StructuredReferenceSelector:1");
    }
}
