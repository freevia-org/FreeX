using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression test for round-26 finding R26-table-structured-ref-deep-1, and round-27 finding
/// R27-meta-3 which found round-26's fix over-corrected, in FormulaSerializer.cs:
///
/// FormulaSerializer.WriteNode's StructuredReferenceNode case never re-escaped '#', a lone '[', or
/// an apostrophe in sr.ColumnName -- it only doubled ']' (and skipped escaping entirely whenever the
/// name contained '[', a heuristic meant for combined/bracketed selectors like "[#Data],[Amount]").
/// Lexer.ReadStructuredReferenceSelectorSlow strips the apostrophe-escape on read (per
/// IsEscapableStructuredReferenceChar's '[', ']', '#', "'" set), so a literal column named e.g.
/// "A[B" (correctly written as Table1[A'[B] in the source formula) loses the escape on any AST
/// rewrite-and-reserialize (table rename, row/column insert-delete, ...), producing text the Lexer
/// cannot even re-parse.
///
/// Round-26 fixed that by re-applying the apostrophe-escape convention (mirroring
/// StructuredTableTotalsCommand.EscapeStructuredReferenceColumnName) to any bare (non-combined)
/// selector -- but it escaped '#' unconditionally, which corrupts a genuine #Data/#Headers/#Totals/
/// #All/#This Row section-keyword reference into an escaped-literal-column reference on every
/// unrelated reserialize (round-27 finding R27-meta-3), since the Lexer strips the escape on read and
/// a literal column named exactly "#Data" is indistinguishable from the keyword at this layer. Fixed
/// by recognizing the fixed set of section keywords and passing them through unescaped, keeping the
/// apostrophe-escape only for selectors that are not a recognized keyword spelling.
/// </summary>
public class R26_FormulaSerializerStructuredReferenceEscapeTests
{
    private static string RoundTrip(string formula)
    {
        var tokens = new Lexer(formula).Tokenize();
        var ast = new Parser(tokens).Parse();
        return FormulaSerializer.Serialize(ast);
    }

    [Fact]
    public void Serialize_LiteralColumnNameMatchingDataKeyword_IsIndistinguishableFromKeyword()
    {
        // R27-meta-3: Lexer.ReadStructuredReferenceSelectorSlow strips the apostrophe-escape on
        // read, so a literal column escaped in source as '#Data and a genuine #Data section
        // keyword both produce the identical ColumnName ("#Data") -- this layer has no way to
        // tell them apart. Re-escaping unconditionally (the round-26 behavior) corrupted the far
        // more common case -- a real #Data/#Headers/#Totals/#All/#This Row keyword reference --
        // into a literal-column escape on every reserialize. The accepted, documented limitation
        // is that a literal column named exactly "#Data" does not perfectly round-trip through a
        // rewrite; it is treated as the keyword, favoring the common case.
        RoundTrip("=SUM(Table1['#Data])").Should().Be("SUM(TABLE1[#Data])");
    }

    [Fact]
    public void Serialize_LiteralColumnNameWithBracket_ProducesParseableRoundTrip()
    {
        // Bug case: a literal column name containing an unescaped '[' (e.g. "A[B", escaped in
        // source as A'[B) must not be written out raw -- the old code's "skip all escaping when the
        // name contains '['" heuristic produced text the Lexer could not even re-parse.
        var serialized = RoundTrip("=Table1[A'[B]");
        serialized.Should().Be("TABLE1[A'[B]");

        // Prove it actually round-trips: re-lex/re-parse the serialized text and confirm the
        // structured reference's column name survives unchanged.
        var reparsed = new Parser(new Lexer("=" + serialized).Tokenize()).Parse();
        reparsed.Should().BeOfType<FreeX.Core.Formula.StructuredReferenceNode>()
            .Which.ColumnName.Should().Be("A[B");
    }

    [Fact]
    public void Serialize_LiteralColumnNameWithApostrophe_EscapesApostrophe()
    {
        // A column literally named "It's" (escaped in source as It''s) must keep its escape.
        RoundTrip("=Table1[It''s]").Should().Be("TABLE1[It''s]");
    }

    [Fact]
    public void Serialize_PlainColumnName_NoEscapableCharacters_Unaffected()
    {
        // Sibling already-working case: an ordinary column name with none of '[', ']', '#', or an
        // apostrophe must round-trip completely unchanged (no spurious escaping introduced).
        RoundTrip("=SUM(Table1[Amount])").Should().Be("SUM(TABLE1[Amount])");
    }

    [Fact]
    public void Serialize_SectionKeywordSelector_StillResolvesAsWholeTableKeyword()
    {
        // R27-meta-3: a genuine #Data section keyword (no escape in the source) must round-trip
        // UNESCAPED and keep meaning "the whole data body". Escaping it (round-26's behavior)
        // turns "TABLE1[#Data]" into "TABLE1['#Data]", which real Excel's structured-reference
        // syntax parses as a literal column named "#Data", not the section keyword -- a
        // meaning-changing corruption on any unrelated reserialize (row/column insert-delete,
        // rename, etc.).
        RoundTrip("=SUM(Table1[#Data])").Should().Be("SUM(TABLE1[#Data])");
    }

    [Fact]
    public void Serialize_CombinedStructuredReference_StillPassedThroughUnescaped()
    {
        // Sibling already-working case (existing FormulaSerializerTests coverage): a combined
        // bracketed selector must still be written through as-is, not double-escaped.
        RoundTrip("=SUM(Sales[[#Data],[Amount]])").Should().Be("SUM(SALES[[#Data],[Amount]])");
    }

    [Theory]
    [InlineData("#Headers")]
    [InlineData("#Totals")]
    [InlineData("#All")]
    [InlineData("#This Row")]
    public void Serialize_OtherSectionKeywordSelectors_RoundTripUnescaped(string keyword)
    {
        // R27-meta-3: every recognized section keyword -- not just #Data -- must round-trip
        // unescaped, matching real Excel's structured-reference keyword syntax.
        RoundTrip($"=SUM(Table1[{keyword}])").Should().Be($"SUM(TABLE1[{keyword}])");
    }
}
