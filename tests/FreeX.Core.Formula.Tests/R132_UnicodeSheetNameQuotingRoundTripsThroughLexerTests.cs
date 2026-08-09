using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R132: end-to-end proof that <see cref="SheetNameFormatter"/> and the <see cref="Lexer"/> now
/// agree on which sheet names round-trip unquoted. Before the fix, the Lexer's
/// ReadIdentifierOrRef continuation scan (Unicode-aware <c>char.IsLetterOrDigit</c>) happily lexed
/// an unquoted Unicode-letter sheet-qualifier like <c>Café!A1</c>, but
/// <see cref="SheetNameFormatter.NeedsQuoting"/> (ASCII-only <c>char.IsAsciiLetterOrDigit</c>) would
/// report that same name as needing quotes -- so any code path that reserializes a formula (e.g.
/// <see cref="FormulaSerializer"/>, used whenever <c>FormulaRewriter</c> reserializes a formula
/// after an unrelated edit shifts a reference) silently rewrote the user's unquoted
/// <c>=Café!A1</c> into <c>='Café'!A1</c> -- a different formula text than what the Lexer itself
/// considers necessary, and a spurious diff on every such round-trip.
/// </summary>
public sealed class R132_UnicodeSheetNameQuotingRoundTripsThroughLexerTests
{
    [Theory]
    [InlineData("Café")]
    [InlineData("Δεδομένα")]
    [InlineData("日本語")]
    public void Lexer_UnquotedUnicodeLetterSheetQualifier_TokenizesWithoutRequiringQuotes(string sheetName)
    {
        // The Lexer accepts this UNQUOTED -- no leading apostrophe at all -- proving the Lexer's own
        // acceptance criteria for this sheet name (the ground truth SheetNameFormatter must match).
        var tokens = new Lexer($"={sheetName}!A1").Tokenize();

        tokens[0].Type.Should().Be(TokenType.SheetQualifier);
        tokens[0].Value.Should().Be(sheetName);
        tokens[1].Type.Should().Be(TokenType.CellRef);
        tokens[1].Value.Should().Be("A1");
    }

    [Theory]
    [InlineData("Café")]
    [InlineData("Δεδομένα")]
    [InlineData("日本語")]
    public void FormulaSerializer_CellRefWithUnicodeLetterSheetName_SerializesUnquoted(string sheetName)
    {
        // FormulaSerializer must agree with the Lexer's own acceptance: writing this sheet name back
        // out must NOT add quotes the Lexer never required reading it in.
        var node = new CellRefNode("A", 1, SheetName: sheetName);

        var serialized = FormulaSerializer.Serialize(node);

        serialized.Should().Be($"{sheetName}!A1");
    }

    [Theory]
    [InlineData("Café")]
    [InlineData("Δεδομένα")]
    [InlineData("日本語")]
    public void UnicodeLetterSheetName_RoundTripsThroughParseAndReserializeUnchanged(string sheetName)
    {
        // Full loop: lex+parse the UNQUOTED form, reserialize, and confirm the text is byte-for-byte
        // identical -- i.e. a FormulaRewriter reserialization pass (triggered by an unrelated edit
        // elsewhere in the same formula) leaves this reference exactly as the user typed it instead
        // of silently adding quotes.
        var formulaText = $"={sheetName}!A1";
        var tokens = new Lexer(formulaText).Tokenize();
        var ast = new Parser(tokens).Parse();

        var reserialized = "=" + FormulaSerializer.Serialize(ast);

        reserialized.Should().Be(formulaText);
    }

    // --- Sibling no-regression: a sheet name the Lexer genuinely requires quoting for (a space,
    // which is never a valid identifier character in ANY position) must still come back quoted. ---

    [Fact]
    public void FormulaSerializer_CellRefWithSpaceInSheetName_StillSerializesQuoted()
    {
        var node = new CellRefNode("A", 1, SheetName: "My Café");

        FormulaSerializer.Serialize(node).Should().Be("'My Café'!A1");
    }
}
