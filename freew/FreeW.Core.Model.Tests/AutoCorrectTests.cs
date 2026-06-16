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

    // --- Double hyphen -> en dash ---

    [Fact]
    public void DoubleHyphen_AfterHyphen_BecomesEnDash()
    {
        var result = AutoCorrect.Evaluate("word-", '-');

        result.Applies.Should().BeTrue();
        result.DeleteBefore.Should().Be(1);
        result.Insert.Should().Be("–"); // en dash U+2013
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
}
