using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression test for round-26 finding R26-table-structured-ref-deep-1 in FormulaSerializer.cs:
///
/// FormulaSerializer.WriteNode's StructuredReferenceNode case never re-escaped '#', a lone '[', or
/// an apostrophe in sr.ColumnName -- it only doubled ']' (and skipped escaping entirely whenever the
/// name contained '[', a heuristic meant for combined/bracketed selectors like "[#Data],[Amount]").
/// Lexer.ReadStructuredReferenceSelectorSlow strips the apostrophe-escape on read (per
/// IsEscapableStructuredReferenceChar's '[', ']', '#', "'" set), so a literal column named e.g.
/// "#Data" (correctly written as Table1['#Data] in the source formula) loses the escape on any
/// AST rewrite-and-reserialize (table rename, row/column insert-delete, ...): the re-serialized
/// text "Table1[#Data]" re-parses to the SAME ColumnName ("#Data") but is no longer distinguishable
/// from -- and evaluates the same as -- the #Data *section keyword* meaning the whole table body,
/// not the literal column. A literal name containing an unescaped '[' (e.g. "A[B") was even worse:
/// the old code wrote it out completely raw, producing text the Lexer cannot parse at all.
///
/// Fixed by re-applying the same apostrophe-escape convention the Lexer understands (mirroring
/// StructuredTableTotalsCommand.EscapeStructuredReferenceColumnName) to any bare (non-combined)
/// selector, while still passing combined/bracketed selectors (which always start with '[') through
/// unescaped so existing combined-selector round-trips are unaffected.
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
    public void Serialize_LiteralColumnNameMatchingDataKeyword_KeepsApostropheEscape()
    {
        // Bug case: a table column literally named "#Data" (escaped in source as '#Data) must
        // still be written back out with the apostrophe escape after passing through the
        // serializer, not as the bare "#Data" text (which reads back as the #Data section keyword).
        RoundTrip("=SUM(Table1['#Data])").Should().Be("SUM(TABLE1['#Data])");
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
        // Sibling already-working case: the #Data section keyword (no escape in the source) must
        // still round-trip and keep meaning "the whole data body" -- unaffected by the fix, since
        // the Lexer treats an escaped or unescaped '#Data' identically (the escape only matters for
        // literal column names that happen to collide with a keyword spelling).
        RoundTrip("=SUM(Table1[#Data])").Should().Be("SUM(TABLE1['#Data])");
    }

    [Fact]
    public void Serialize_CombinedStructuredReference_StillPassedThroughUnescaped()
    {
        // Sibling already-working case (existing FormulaSerializerTests coverage): a combined
        // bracketed selector must still be written through as-is, not double-escaped.
        RoundTrip("=SUM(Sales[[#Data],[Amount]])").Should().Be("SUM(SALES[[#Data],[Amount]])");
    }
}
