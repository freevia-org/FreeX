namespace FreeW.Core.Model.Tests;

public class AutoCorrectTests
{
    // --- Smart quotes: open vs close decision ---

    [Theory]
    [InlineData("", '"', '“')]            // start of paragraph -> opening
    [InlineData("He said ", '"', '“')]    // after whitespace -> opening
    [InlineData("(", '"', '“')]           // after opening punctuation -> opening
    [InlineData("[", '"', '“')]           // after opening bracket -> opening
    [InlineData("{", '"', '“')]           // after opening brace -> opening
    [InlineData("word", '"', '”')]        // after a letter -> closing
    [InlineData("end.", '"', '”')]        // after punctuation -> closing
    public void SmartQuote_Double_DecidesOpenVsClose(string before, char typed, char expected)
    {
        var result = AutoCorrect.Evaluate(before, typed);

        result.Applies.Should().BeTrue();
        result.DeleteBefore.Should().Be(0);
        result.Insert.Should().Be(expected.ToString());
    }

    [Theory]
    [InlineData("", '\'', '‘')]           // start of paragraph -> opening
    [InlineData("it was ", '\'', '‘')]    // after whitespace -> opening
    [InlineData("don", '\'', '’')]        // after a letter -> closing (e.g. don't)
    public void SmartQuote_Single_DecidesOpenVsClose(string before, char typed, char expected)
    {
        var result = AutoCorrect.Evaluate(before, typed);

        result.Applies.Should().BeTrue();
        result.DeleteBefore.Should().Be(0);
        result.Insert.Should().Be(expected.ToString());
    }

    [Fact]
    public void SmartQuote_AfterAnotherOpeningQuote_Opens()
    {
        // Nested quote: "‘  — typed single quote right after an opening double quote should open.
        var result = AutoCorrect.Evaluate("“", '\'');

        result.Insert.Should().Be("‘");
    }

    // --- Double hyphen -> dash ---

    [Fact]
    public void DoubleHyphen_AfterHyphen_BecomesEmDash()
    {
        // "word--" (the dashes hug the word, no surrounding spaces) matches Word's classic
        // "type -- for a dash" shortcut, which produces an em dash.
        var result = AutoCorrect.Evaluate("word-", '-');

        result.Applies.Should().BeTrue();
        result.DeleteBefore.Should().Be(1);
        result.Insert.Should().Be("—"); // em dash U+2014
    }

    [Fact]
    public void SingleHyphen_DoesNotTrigger()
    {
        var result = AutoCorrect.Evaluate("word", '-');

        result.Applies.Should().BeFalse();
    }

    // --- (c) (r) (tm) symbols ---

    [Theory]
    [InlineData("(c", "©", 2)]
    [InlineData("(C", "©", 2)]
    [InlineData("(r", "®", 2)]
    [InlineData("(R", "®", 2)]
    [InlineData("(tm", "™", 3)]
    [InlineData("(TM", "™", 3)]
    [InlineData("(Tm", "™", 3)]
    public void Symbol_OnClosingParen_Completes(string before, string expected, int deleteBefore)
    {
        var result = AutoCorrect.Evaluate(before, ')');

        result.Applies.Should().BeTrue();
        result.DeleteBefore.Should().Be(deleteBefore);
        result.Insert.Should().Be(expected);
    }

    [Fact]
    public void Symbol_UnknownParenContent_DoesNotTrigger()
    {
        AutoCorrect.Evaluate("(x", ')').Applies.Should().BeFalse();
    }

    // --- Ellipsis ---

    [Fact]
    public void Ellipsis_OnThirdPeriod_Collapses()
    {
        var result = AutoCorrect.Evaluate("wait..", '.');

        result.Applies.Should().BeTrue();
        result.DeleteBefore.Should().Be(2);
        result.Insert.Should().Be("…"); // U+2026
    }

    [Fact]
    public void Ellipsis_SinglePeriod_DoesNotTrigger()
    {
        AutoCorrect.Evaluate("done", '.').Applies.Should().BeFalse();
        AutoCorrect.Evaluate("done.", '.').Applies.Should().BeFalse(); // only two so far -> no
    }

    // --- Sentence capitalization ---

    [Fact]
    public void Capitalize_AtParagraphStart_UpperCasesLowercaseLetter()
    {
        var result = AutoCorrect.Evaluate("", 'h');

        result.Applies.Should().BeTrue();
        result.DeleteBefore.Should().Be(0);
        result.Insert.Should().Be("H");
    }

    [Theory]
    [InlineData("Hello. ")]
    [InlineData("Stop! ")]
    [InlineData("Really? ")]
    public void Capitalize_AfterSentenceTerminatorAndSpace_UpperCases(string before)
    {
        var result = AutoCorrect.Evaluate(before, 'w');

        result.Applies.Should().BeTrue();
        result.Insert.Should().Be("W");
    }

    [Fact]
    public void Capitalize_MidWord_DoesNotTrigger()
    {
        AutoCorrect.Evaluate("hel", 'l').Applies.Should().BeFalse();
    }

    [Fact]
    public void Capitalize_AfterCommaSpace_DoesNotTrigger()
    {
        AutoCorrect.Evaluate("yes, ", 'a').Applies.Should().BeFalse();
    }

    [Fact]
    public void Capitalize_AlreadyUppercase_DoesNotTrigger()
    {
        // An upper-case letter at a sentence start needs no correction.
        AutoCorrect.Evaluate("", 'H').Applies.Should().BeFalse();
    }

    // --- No-op ---

    [Fact]
    public void Evaluate_OrdinaryCharacter_IsNoOp()
    {
        // A plain space mid-sentence triggers nothing.
        AutoCorrect.Evaluate("hello", ' ').Applies.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_NullTextBefore_TreatedAsParagraphStart()
    {
        // A null "text before" is the start of a paragraph: a quote opens, a letter capitalizes.
        AutoCorrect.Evaluate(null, '"').Insert.Should().Be("“");
        AutoCorrect.Evaluate(null, 'a').Insert.Should().Be("A");
    }

    [Fact]
    public void AutoCorrectResult_ListOutcome_ConsumesOnlyTheLeadingMarker()
    {
        var result = AutoCorrect.Evaluate("*", ' ');

        result.Outcome.Should().Be(AutoFormatOutcomeKind.BulletList);
        result.DeleteBefore.Should().Be(1);
        result.Insert.Should().BeEmpty();
    }

    // ── AutoFormat-As-You-Type: dashes en vs. em ───────────────────────────────────────────────────────

    [Fact]
    public void DoubleHyphen_BetweenWords_IsEmDash()
    {
        // "word--" (the dashes hug the word, no spaces) → em dash, matching real Word's AutoFormat.
        AutoCorrect.Evaluate("word-", '-').Insert.Should().Be("—");
    }

    [Fact]
    public void DoubleHyphen_SpaceFlanked_IsEnDash()
    {
        // "word --" (a space precedes the double hyphen) → en dash, matching real Word's AutoFormat.
        // Only the two hyphens are replaced; the surrounding spaces are untouched.
        var result = AutoCorrect.Evaluate("word -", '-');
        result.DeleteBefore.Should().Be(1);
        result.Insert.Should().Be("–");
    }

    // ── Automatic bulleted lists ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("*")]
    [InlineData("-")]
    [InlineData(">")]
    public void BulletMarker_AtParagraphStart_RequestsBulletList(string before)
    {
        var result = AutoCorrect.Evaluate(before, ' ');

        result.Applies.Should().BeTrue();
        result.Outcome.Should().Be(AutoFormatOutcomeKind.BulletList);
        result.DeleteBefore.Should().Be(before.Length); // the marker is consumed
        result.Insert.Should().BeEmpty();
    }

    [Fact]
    public void BulletMarker_MidLine_DoesNotTrigger()
    {
        // "a -" is not at the paragraph start, so the space does not start a list.
        AutoCorrect.Evaluate("a -", ' ').Applies.Should().BeFalse();
    }

    // ── Automatic numbered lists ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.")]
    [InlineData("1)")]
    public void NumberMarker_AtParagraphStart_RequestsNumberList(string before)
    {
        var result = AutoCorrect.Evaluate(before, ' ');

        result.Outcome.Should().Be(AutoFormatOutcomeKind.NumberList);
        result.DeleteBefore.Should().Be(before.Length);
        result.Insert.Should().BeEmpty();
    }

    [Fact]
    public void NumberMarker_NonOne_DoesNotTrigger()
    {
        // Only a leading "1." auto-starts a list (so an in-progress "2." edit is left alone).
        AutoCorrect.Evaluate("2.", ' ').Applies.Should().BeFalse();
    }

    // ── Ordinals → superscript ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1st", 2)]
    [InlineData("2nd", 2)]
    [InlineData("3rd", 2)]
    [InlineData("4th", 2)]
    [InlineData("11th", 2)]
    [InlineData("21st", 2)]
    [InlineData("103rd", 2)]
    public void Ordinal_OnSpace_SuperscriptsSuffix(string word, int suffixLength)
    {
        var result = AutoCorrect.Evaluate(word, ' ');

        result.Applies.Should().BeTrue();
        result.Outcome.Should().Be(AutoFormatOutcomeKind.SuperscriptSuffix);
        result.DeleteBefore.Should().Be(word.Length);
        result.Insert.Should().Be(word + " ");
        result.SuffixLength.Should().Be(suffixLength);
    }

    [Theory]
    [InlineData("1th")]   // wrong suffix for 1 (should be st)
    [InlineData("2st")]   // wrong suffix for 2 (should be nd)
    [InlineData("11st")]  // 11 is always th
    [InlineData("abc")]   // not numeric
    public void Ordinal_WrongOrNonOrdinal_DoesNotTrigger(string word)
    {
        AutoCorrect.Evaluate(word, ' ').Applies.Should().BeFalse();
    }

    [Fact]
    public void Ordinal_GluedToLetters_DoesNotTrigger()
    {
        // "x1st" is not a word boundary before the number, so no ordinal.
        AutoCorrect.Evaluate("x1st", ' ').Applies.Should().BeFalse();
    }

    [Fact]
    public void Ordinal_AfterSpace_Triggers()
    {
        var result = AutoCorrect.Evaluate("the 2nd", ' ');
        result.Outcome.Should().Be(AutoFormatOutcomeKind.SuperscriptSuffix);
        result.Insert.Should().Be("2nd ");
    }

    // ── Fractions → glyph ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1/2", "½")]
    [InlineData("1/4", "¼")]
    [InlineData("3/4", "¾")]
    public void Fraction_OnSpace_BecomesGlyph(string before, string glyph)
    {
        var result = AutoCorrect.Evaluate(before, ' ');

        result.Applies.Should().BeTrue();
        result.DeleteBefore.Should().Be(before.Length);
        result.Insert.Should().Be(glyph + " ");
        result.Outcome.Should().Be(AutoFormatOutcomeKind.None);
    }

    [Theory]
    [InlineData("11/2")]  // no dedicated glyph / not a clean fraction at a boundary
    [InlineData("1/3")]   // no single glyph
    public void Fraction_NoGlyphOrGlued_DoesNotTrigger(string before)
    {
        AutoCorrect.Evaluate(before, ' ').Applies.Should().BeFalse();
    }

    // ── Hyperlinks ─────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("see http://example.com", "http://example.com")]
    [InlineData("go https://www.example.com/path", "https://www.example.com/path")]
    public void Hyperlink_Url_OnSpace_Links(string before, string expectedTarget)
    {
        var result = AutoCorrect.Evaluate(before, ' ');

        result.Outcome.Should().Be(AutoFormatOutcomeKind.Hyperlink);
        result.LinkTarget.Should().Be(expectedTarget);
        result.Insert.Should().EndWith(" ");
    }

    [Fact]
    public void Hyperlink_BareWww_GetsHttpScheme()
    {
        var result = AutoCorrect.Evaluate("visit www.example.com", ' ');
        result.Outcome.Should().Be(AutoFormatOutcomeKind.Hyperlink);
        result.LinkTarget.Should().Be("http://www.example.com");
    }

    [Fact]
    public void Hyperlink_Email_GetsMailto()
    {
        var result = AutoCorrect.Evaluate("mail me@example.com", ' ');
        result.Outcome.Should().Be(AutoFormatOutcomeKind.Hyperlink);
        result.LinkTarget.Should().Be("mailto:me@example.com");
    }

    [Theory]
    [InlineData("just a word")]
    [InlineData("not.a.url")]      // dots but no scheme/www/@
    [InlineData("@example.com")]   // empty local part
    public void Hyperlink_NonLink_DoesNotTrigger(string before)
    {
        AutoCorrect.Evaluate(before, ' ').Applies.Should().BeFalse();
    }

    [Fact]
    public void LinkTargetFor_IsPureAndReusable()
    {
        AutoCorrect.LinkTargetFor("https://a.com").Should().Be("https://a.com");
        AutoCorrect.LinkTargetFor("www.a.com").Should().Be("http://www.a.com");
        AutoCorrect.LinkTargetFor("a@b.com").Should().Be("mailto:a@b.com");
        AutoCorrect.LinkTargetFor("plain").Should().BeNull();
    }

    // ── Per-rule toggles: a disabled rule is a no-op ───────────────────────────────────────────────────

    [Fact]
    public void DisabledRule_IsNoOp_PerRule()
    {
        AutoCorrect.Evaluate("word", '"', AutoFormatOptions.Default with { SmartQuotes = false }).Applies.Should().BeFalse();
        AutoCorrect.Evaluate("word-", '-', AutoFormatOptions.Default with { Dashes = false }).Applies.Should().BeFalse();
        AutoCorrect.Evaluate("wait..", '.', AutoFormatOptions.Default with { Ellipsis = false }).Applies.Should().BeFalse();
        AutoCorrect.Evaluate("(c", ')', AutoFormatOptions.Default with { Symbols = false }).Applies.Should().BeFalse();
        AutoCorrect.Evaluate("", 'h', AutoFormatOptions.Default with { Capitalization = false }).Applies.Should().BeFalse();
        AutoCorrect.Evaluate("*", ' ', AutoFormatOptions.Default with { BulletedLists = false }).Applies.Should().BeFalse();
        AutoCorrect.Evaluate("1.", ' ', AutoFormatOptions.Default with { NumberedLists = false }).Applies.Should().BeFalse();
        AutoCorrect.Evaluate("1st", ' ', AutoFormatOptions.Default with { Ordinals = false }).Applies.Should().BeFalse();
        AutoCorrect.Evaluate("1/2", ' ', AutoFormatOptions.Default with { Fractions = false }).Applies.Should().BeFalse();
        AutoCorrect.Evaluate("http://x.com", ' ', AutoFormatOptions.Default with { Hyperlinks = false }).Applies.Should().BeFalse();
    }

    [Fact]
    public void AllOff_SuppressesEveryRule()
    {
        AutoCorrect.Evaluate("word", '"', AutoFormatOptions.AllOff).Applies.Should().BeFalse();
        AutoCorrect.Evaluate("1st", ' ', AutoFormatOptions.AllOff).Applies.Should().BeFalse();
        AutoCorrect.Evaluate("", 'h', AutoFormatOptions.AllOff).Applies.Should().BeFalse();
    }
}
