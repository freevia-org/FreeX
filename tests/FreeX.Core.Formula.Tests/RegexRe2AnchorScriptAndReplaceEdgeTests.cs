using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Covers round-43 formula-regex-2 findings (BuiltInFunctions.Regex.cs):
///  - R43-formula-regex-2-1: atomic groups (?&gt;...) are not valid RE2 syntax and must be
///    rejected with #VALUE!, like the existing backreference/lookaround checks.
///  - R43-formula-regex-2-2: '$' must behave as RE2's strict end-of-text anchor (like \z), not
///    .NET's default "end of string, or immediately before one trailing newline" anchor.
///  - R43-formula-regex-2-3: RE2 Unicode *script* names in \p{Name} (e.g. \p{Greek}) must be
///    accepted, not rejected with a spurious #VALUE!.
///  - R43-formula-regex-2-4: REGEXREPLACE must substitute an empty string for an out-of-range
///    $N group reference in the replacement text, per RE2/Go's regexp.Expand semantics, instead
///    of leaving the literal "$N" text untouched like .NET's Match.Result does.
/// </summary>
public sealed class RegexRe2AnchorScriptAndReplaceEdgeTests
{
    private readonly FormulaEvaluator _eval = new();

    // ---- R43-formula-regex-2-1: atomic groups rejected -----------------------------------

    [Theory]
    [InlineData("=REGEXTEST(\"aaa\",\"(?>a+)\")")]
    [InlineData("=REGEXEXTRACT(\"aaa\",\"(?>a+)\")")]
    [InlineData("=REGEXREPLACE(\"aaa\",\"(?>a+)\",\"x\")")]
    public void RegexFunctions_AtomicGroup_ReturnsValueLikeRe2(string formula)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void RegexTest_NonAtomicGroupWithGreaterThan_StillMatchesNormally()
    {
        // Sibling/no-regression case: a literal '>' following a normal (non-"(?") group must not
        // be mistaken for an atomic group -- only "(?>" trips the new check.
        _eval.Evaluate("=REGEXTEST(\"a>b\",\"(a)>b\")", Sheet()).Should().Be(new BoolValue(true));
    }

    // ---- R43-formula-regex-2-2: strict end-of-text '$' ------------------------------------

    [Fact]
    public void RegexTest_DollarAnchor_DoesNotMatchBeforeTrailingNewline()
    {
        // Real Excel's RE2 '$' is a strict end-of-text anchor (like \z), unlike .NET's default
        // '$' which also matches just before a single trailing '\n'.
        _eval.Evaluate("=REGEXTEST(\"Total: 100\" & CHAR(10),\"100$\")", Sheet()).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void RegexTest_DollarAnchor_StillMatchesTrueEndOfText()
    {
        // Sibling/no-regression case: '$' must still match the actual end of the string when
        // there is no trailing newline.
        _eval.Evaluate("=REGEXTEST(\"Total: 100\",\"100$\")", Sheet()).Should().Be(new BoolValue(true));
    }

    // ---- R43-formula-regex-2-3: Unicode script names in \p{} ------------------------------

    [Fact]
    public void RegexTest_UnicodeScriptName_Greek_MatchesGreekLetter()
    {
        _eval.Evaluate("=REGEXTEST(\"α\",\"\\p{Greek}\")", Sheet()).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void RegexTest_UnicodeScriptName_Greek_DoesNotMatchLatinLetter()
    {
        // Sibling/no-regression case: the script-name translation must still correctly
        // discriminate -- a plain Latin letter must not match \p{Greek}.
        _eval.Evaluate("=REGEXTEST(\"a\",\"\\p{Greek}\")", Sheet()).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void RegexTest_UnicodeGeneralCategory_StillWorksUnaffectedByScriptTranslation()
    {
        // Sibling/no-regression case: ordinary .NET-native \p{} general categories (already
        // valid RE2 syntax) must keep working exactly as before.
        _eval.Evaluate("=REGEXTEST(\"5\",\"\\p{N}\")", Sheet()).Should().Be(new BoolValue(true));
    }

    // ---- R43-formula-regex-2-4: out-of-range $N in replacement -----------------------------

    [Fact]
    public void RegexReplace_OutOfRangeGroupReference_SubstitutesEmptyStringNotLiteralText()
    {
        _eval.Evaluate("=REGEXREPLACE(\"ab\",\"(a)(b)\",\"$3\")", Sheet())
            .Should().Be(new TextValue(""));
    }

    [Fact]
    public void RegexReplace_CurrencyLikeReplacementWithOutOfRangeGroup_DropsOnlyTheGroupReference()
    {
        // "$3.00" with only 2 capture groups: '$3' is an unresolved group reference (-> empty),
        // and the literal ".00" that follows is left untouched.
        _eval.Evaluate("=REGEXREPLACE(\"ab\",\"(a)(b)\",\"$3.00\")", Sheet())
            .Should().Be(new TextValue(".00"));
    }

    [Fact]
    public void RegexReplace_InRangeGroupReferences_StillSubstituteNormally()
    {
        // Sibling/no-regression case: existing in-range numbered group references (already
        // covered elsewhere for the swap case) must keep substituting their captured values.
        _eval.Evaluate("=REGEXREPLACE(\"John Smith\",\"(\\w+)\\s+(\\w+)\",\"$2, $1\")", Sheet())
            .Should().Be(new TextValue("Smith, John"));
    }

    [Fact]
    public void RegexReplace_DoubleDollarLiteral_StillProducesLiteralDollarSign()
    {
        // Sibling/no-regression case: '$$' must still expand to a literal '$', not be treated as
        // an (out-of-range) group reference.
        _eval.Evaluate("=REGEXREPLACE(\"ab\",\"(a)(b)\",\"$$5\")", Sheet())
            .Should().Be(new TextValue("$5"));
    }

    private static Sheet Sheet(params (int Row, int Col, ScalarValue Value)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), value);
        return sheet;
    }
}
