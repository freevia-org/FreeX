using System.Collections.Generic;

namespace FreeW.Core.Model.Tests;

/// <summary>
/// Unit tests for the pure Word-"AutoCorrect"-tab engine (<see cref="AutoCorrectEngine"/>): the replace-text
/// table, the two-initial-capitals fix, and day-name capitalization — distinct from the AutoFormat-As-You-Type
/// rules covered by <c>AutoCorrectTests</c>.
/// </summary>
public class AutoCorrectEngineTests
{
    // ── Replace text as you type ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("teh", ' ', "the ")]
    [InlineData("adn", ' ', "and ")]
    [InlineData("recieve", ' ', "receive ")]
    [InlineData("seperate", ' ', "separate ")]
    public void Replace_KnownTypo_OnSpace_Corrects(string word, char sep, string expectedInsert)
    {
        var result = AutoCorrectEngine.Evaluate(word, sep);

        result.Applies.Should().BeTrue();
        result.DeleteBefore.Should().Be(word.Length);
        result.Insert.Should().Be(expectedInsert);
        result.Outcome.Should().Be(AutoFormatOutcomeKind.None);
    }

    [Theory]
    [InlineData('.')]
    [InlineData(',')]
    [InlineData('!')]
    [InlineData(')')]
    public void Replace_CompletedByPunctuation_ReEmitsTheSeparator(char sep)
    {
        var result = AutoCorrectEngine.Evaluate("teh", sep);

        result.Applies.Should().BeTrue();
        result.Insert.Should().Be("the" + sep);
    }

    [Fact]
    public void Replace_PreservesLeadingCapital()
    {
        // "Teh" at a sentence start → "The"; the lowercase table entry inherits the typed capital.
        AutoCorrectEngine.Evaluate("Teh", ' ').Insert.Should().Be("The ");
    }

    [Fact]
    public void Replace_GlyphEntry_KeepsExactCasing()
    {
        // Symbol/arrow entries are not letter words, so leading-case matching leaves them untouched.
        AutoCorrectEngine.Evaluate("(c)", ' ').Insert.Should().Be("© ");
        AutoCorrectEngine.Evaluate("-->", ' ').Insert.Should().Be("→ ");
    }

    [Fact]
    public void Replace_OnlyOnSeparator_NotMidWord()
    {
        // Typing a letter mid-word never fires AutoCorrect (no word boundary yet).
        AutoCorrectEngine.Evaluate("te", 'h').Applies.Should().BeFalse();
    }

    [Fact]
    public void Replace_UnknownWord_IsNoOp()
    {
        AutoCorrectEngine.Evaluate("hello", ' ').Applies.Should().BeFalse();
    }

    [Fact]
    public void Replace_OnlyTrailingWordIsConsidered()
    {
        // "I teh" → only the trailing "teh" is replaced; the leading text and its space are untouched.
        var result = AutoCorrectEngine.Evaluate("I teh", ' ');
        result.Applies.Should().BeTrue();
        result.DeleteBefore.Should().Be(3);
        result.Insert.Should().Be("the ");
    }

    [Fact]
    public void Replace_Disabled_IsNoOp()
    {
        var opts = AutoCorrectOptions.Default;
        opts.ReplaceText = false;
        AutoCorrectEngine.Evaluate("teh", ' ', opts).Applies.Should().BeFalse();
    }

    [Fact]
    public void Replace_CustomTable_Matches()
    {
        var opts = new AutoCorrectOptions
        {
            ReplaceText = true,
            CorrectTwoInitialCapitals = false,
            CapitalizeDayNames = false,
            Replacements = new List<AutoCorrectReplacement> { new("btw", "by the way") },
        };
        AutoCorrectEngine.Evaluate("btw", ' ', opts).Insert.Should().Be("by the way ");
    }

    // ── Correct TWo INitial CApitals ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("TWo", "Two")]
    [InlineData("INitial", "Initial")]
    [InlineData("CApitals", "Capitals")]
    [InlineData("THe", "The")]
    public void TwoInitialCaps_OnSpace_LowercasesSecondCapital(string word, string expected)
    {
        var result = AutoCorrectEngine.Evaluate(word, ' ');

        result.Applies.Should().BeTrue();
        result.DeleteBefore.Should().Be(word.Length);
        result.Insert.Should().Be(expected + " ");
    }

    [Theory]
    [InlineData("USA")]    // all caps — an acronym, left alone
    [InlineData("The")]    // single leading capital — already correct
    [InlineData("hi")]     // too short / lowercase
    [InlineData("ABCd")]   // three leading caps — not a two-initial slip
    public void TwoInitialCaps_NonSlip_IsNoOp(string word)
    {
        AutoCorrectEngine.Evaluate(word, ' ', new AutoCorrectOptions
        {
            CorrectTwoInitialCapitals = true,
            CapitalizeDayNames = false,
            ReplaceText = false,
            Replacements = new List<AutoCorrectReplacement>(),
        }).Applies.Should().BeFalse();
    }

    [Fact]
    public void TwoInitialCaps_Disabled_IsNoOp()
    {
        var opts = AutoCorrectOptions.Default;
        opts.CorrectTwoInitialCapitals = false;
        AutoCorrectEngine.Evaluate("TWo", ' ', opts).Applies.Should().BeFalse();
    }

    // ── Capitalize names of days ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("monday", "Monday")]
    [InlineData("tuesday", "Tuesday")]
    [InlineData("wednesday", "Wednesday")]
    [InlineData("thursday", "Thursday")]
    [InlineData("friday", "Friday")]
    [InlineData("saturday", "Saturday")]
    [InlineData("sunday", "Sunday")]
    public void DayName_Lowercase_OnSpace_Capitalizes(string word, string expected)
    {
        var result = AutoCorrectEngine.Evaluate(word, ' ');

        result.Applies.Should().BeTrue();
        result.Insert.Should().Be(expected + " ");
    }

    [Fact]
    public void DayName_AlreadyCapitalized_IsNoOp()
    {
        AutoCorrectEngine.Evaluate("Monday", ' ').Applies.Should().BeFalse();
    }

    [Fact]
    public void DayName_NotADay_IsNoOp()
    {
        AutoCorrectEngine.Evaluate("someday", ' ').Applies.Should().BeFalse();
    }

    [Fact]
    public void DayName_Disabled_IsNoOp()
    {
        var opts = AutoCorrectOptions.Default;
        opts.CapitalizeDayNames = false;
        // Disable replace too so a "monday" entry can't sneak in (there isn't one, but be explicit).
        opts.ReplaceText = false;
        AutoCorrectEngine.Evaluate("monday", ' ', opts).Applies.Should().BeFalse();
    }

    // ── Priority + boundaries ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ReplaceTable_WinsOverCapitalizationRules()
    {
        // A table entry takes priority over the two-caps / day rules.
        var opts = new AutoCorrectOptions
        {
            ReplaceText = true,
            CorrectTwoInitialCapitals = true,
            CapitalizeDayNames = true,
            Replacements = new List<AutoCorrectReplacement> { new("TWo", "DEUX") },
        };
        AutoCorrectEngine.Evaluate("TWo", ' ', opts).Insert.Should().Be("DEUX ");
    }

    [Fact]
    public void NonSeparatorChar_IsNoOp()
    {
        // A letter never triggers a word-completion rule.
        AutoCorrectEngine.Evaluate("teh", 'x').Applies.Should().BeFalse();
    }

    [Fact]
    public void EmptyTrailingWord_IsNoOp()
    {
        // A separator right after another separator (double space) has no word to correct.
        AutoCorrectEngine.Evaluate("teh ", ' ').Applies.Should().BeFalse();
    }

    [Fact]
    public void NullTextBefore_IsNoOp()
    {
        AutoCorrectEngine.Evaluate(null, ' ').Applies.Should().BeFalse();
    }

    [Fact]
    public void AllOff_SuppressesEveryRule()
    {
        AutoCorrectEngine.Evaluate("teh", ' ', AutoCorrectOptions.AllOff).Applies.Should().BeFalse();
        AutoCorrectEngine.Evaluate("TWo", ' ', AutoCorrectOptions.AllOff).Applies.Should().BeFalse();
        AutoCorrectEngine.Evaluate("monday", ' ', AutoCorrectOptions.AllOff).Applies.Should().BeFalse();
    }

    // ── Options normalization ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_DropsBlankAndDuplicateEntries_LastWins()
    {
        var opts = new AutoCorrectOptions
        {
            Replacements = new List<AutoCorrectReplacement>
            {
                new("teh", "the"),
                new("  ", "blank-key-dropped"),
                new("x", "  "),                  // blank value kept? value is whitespace -> kept (non-empty)
                new("TEH", "THE-override"),      // duplicate key (case-insensitive) -> last wins
                new("", "empty-dropped"),
            },
        };

        opts.Normalize();

        opts.Replacements.Should().ContainSingle(r => r.Replace == "TEH" && r.With == "THE-override");
        opts.Replacements.Should().NotContain(r => string.IsNullOrWhiteSpace(r.Replace));
    }
}
