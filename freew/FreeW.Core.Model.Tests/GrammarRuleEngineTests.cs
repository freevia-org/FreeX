namespace FreeW.Core.Model.Tests;

public sealed class GrammarRuleEngineTests
{
    [Fact]
    public void Rules_have_unique_ids_messages_and_cover_every_known_rule_id()
    {
        GrammarRuleEngine.Rules.Select(rule => rule.Id).Should().OnlyHaveUniqueItems();
        GrammarRuleEngine.Rules.Should().OnlyContain(rule => !string.IsNullOrWhiteSpace(rule.Message));
        GrammarRuleEngine.Rules.Select(rule => rule.Id).Should().BeEquivalentTo(
        [
            GrammarRuleIds.RepeatedWord,
            GrammarRuleIds.SentenceCapitalization,
            GrammarRuleIds.MultipleSpaces,
            GrammarRuleIds.SpaceBeforePunctuation,
            GrammarRuleIds.MissingSpaceAfterPunctuation,
            GrammarRuleIds.IndefiniteArticle,
            GrammarRuleIds.RepeatedPunctuation,
            GrammarRuleIds.UnpairedBracket,
            GrammarRuleIds.UnpairedQuote,
            GrammarRuleIds.CommonConfusion,
        ]);
        GrammarRuleEngine.FindRule(GrammarRuleIds.MultipleSpaces)!.Message.Should().Contain("space");
        GrammarRuleEngine.FindRule("nope").Should().BeNull();
    }

    [Theory]
    [InlineData("Clean, ordinary sentence. Another one follows! Is it fine? Yes.")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Visit https://example.com/a,b?x=1.Next or www.example.org/page.html today.")]
    [InlineData("Mail first.last@example.com,then wait.")]
    [InlineData("It costs 1,000.50 at 10:30,roughly 3.14 times v1.2.3 more.")]
    [InlineData("Use e.g. apples, i.e. fruit, in the U.S. and the U.K. every day.")]
    [InlineData("Call obj.Method() or std::vector<int>, set snake_case=1 and read file.txt or src/app/main.cs.")]
    [InlineData("Dr. smith met Mr. jones at approx. noon.")]
    [InlineData("She paused... then went on. He agreed.")]
    [InlineData("He owns an iPhone and a MacBook.")]
    public void Check_is_silent_for_correct_text_and_protected_tokens(string text)
    {
        GrammarRuleEngine.Check(text).Should().BeEmpty();
    }

    // ── Multiple spaces ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MultipleSpaces_flags_the_neighbouring_words_and_replaces_only_the_spaces()
    {
        var issue = Single("The quick  brown fox", GrammarRuleIds.MultipleSpaces);

        issue.Text.Should().Be("quick  brown");
        issue.Start.Should().Be(4);
        issue.Message.Should().NotBeNullOrWhiteSpace();
        issue.Suggestions.Should().Equal(new ProofingSuggestion(" ", 9, 2));
        Apply("The quick  brown fox", issue).Should().Be("The quick brown fox");
    }

    [Theory]
    [InlineData("  Indented line")]
    [InlineData("Trailing spaces  ")]
    [InlineData("End of sentence.  Next sentence.")]
    [InlineData("Label:  value")]
    [InlineData("Name      Age      City")]
    [InlineData("Tab\t  mix")]
    public void MultipleSpaces_ignores_indentation_alignment_and_sentence_spacing(string text)
    {
        Issues(text, GrammarRuleIds.MultipleSpaces).Should().BeEmpty();
    }

    // ── Space before punctuation ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Hello , world", "Hello ,", "Hello, world")]
    [InlineData("Really ?", "Really ?", "Really?")]
    [InlineData("It works .", "works .", "It works.")]
    [InlineData("Wait  ; then go", "Wait  ;", "Wait; then go")]
    [InlineData("An apple ,then a pear", "apple ,", "An apple, then a pear")]
    public void SpaceBeforePunctuation_flags_and_removes_the_space(string text, string flagged, string corrected)
    {
        var issue = Single(text, GrammarRuleIds.SpaceBeforePunctuation);

        issue.Text.Should().Be(flagged);
        Apply(text, issue).Should().Be(corrected);
    }

    [Theory]
    [InlineData("Built on .NET and .5 scale")]
    [InlineData("Then ... silence")]
    [InlineData("A spaced . . . ellipsis")]
    [InlineData("Smile :) and wink ;)")]
    [InlineData("Ratio a : b")]
    public void SpaceBeforePunctuation_ignores_leading_dots_ellipses_and_emoticons(string text)
    {
        Issues(text, GrammarRuleIds.SpaceBeforePunctuation).Should().BeEmpty();
    }

    // ── Missing space after punctuation ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Hello,world", "Hello,world", "Hello, world")]
    [InlineData("It ended.Then it began", "ended.Then", "It ended. Then it began")]
    [InlineData("Stop;go", "Stop;go", "Stop; go")]
    [InlineData("Note:this matters", "Note:this", "Note: this matters")]
    [InlineData("What?nothing", "What?nothing", "What? nothing")]
    public void MissingSpaceAfterPunctuation_inserts_a_space(string text, string flagged, string corrected)
    {
        var issue = Single(text, GrammarRuleIds.MissingSpaceAfterPunctuation);

        issue.Text.Should().Be(flagged);
        issue.Suggestions.Should().ContainSingle().Which.Length.Should().Be(0);
        Apply(text, issue).Should().Be(corrected);
    }

    [Theory]
    [InlineData("Open example.com or readme.txt")]
    [InlineData("At 10:30,5 people ate 1,000 nuts")]
    [InlineData("See e.g.,this and i.e.,that")]
    [InlineData("Run System.IO.File.Open or a::b")]
    [InlineData("C:\\Users\\name and mailto:me@example.com")]
    [InlineData("What?! No.")]
    public void MissingSpaceAfterPunctuation_ignores_urls_numbers_abbreviations_and_code(string text)
    {
        Issues(text, GrammarRuleIds.MissingSpaceAfterPunctuation).Should().BeEmpty();
    }

    // ── Repeated punctuation ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Apples,, pears", ",,", ",", "Apples, pears")]
    [InlineData("First;; second", ";;", ";", "First; second")]
    [InlineData("The end..", "..", ".", "The end.")]
    public void RepeatedPunctuation_collapses_the_run(string text, string flagged, string replacement, string corrected)
    {
        var issue = Single(text, GrammarRuleIds.RepeatedPunctuation);

        issue.Text.Should().Be(flagged);
        issue.Suggestions[0].Replacement.Should().Be(replacement);
        Apply(text, issue).Should().Be(corrected);
    }

    [Fact]
    public void RepeatedPunctuation_offers_an_ellipsis_for_two_dots()
    {
        Single("Well..", GrammarRuleIds.RepeatedPunctuation).Suggestions
            .Select(suggestion => suggestion.Replacement).Should().Equal(".", "...");
    }

    [Theory]
    [InlineData("Wait... what")]
    [InlineData("Really?! Yes!!")]
    [InlineData("Range 1..5 and path ../up")]
    public void RepeatedPunctuation_ignores_ellipses_emphasis_and_ranges(string text)
    {
        Issues(text, GrammarRuleIds.RepeatedPunctuation).Should().BeEmpty();
    }

    // ── Sentence capitalization ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("It rained. the game stopped.", "the", 11, "It rained. The game stopped.")]
    [InlineData("Really? yes, really.", "yes", 8, "Really? Yes, really.")]
    [InlineData("the game was over.", "the", 0, "The game was over.")]
    [InlineData("Done! it's over.", "it's", 6, "Done! It's over.")]
    public void SentenceCapitalization_flags_the_lower_case_opener(string text, string flagged, int start, string corrected)
    {
        var issue = Single(text, GrammarRuleIds.SentenceCapitalization);

        issue.Text.Should().Be(flagged);
        issue.Start.Should().Be(start);
        Apply(text, issue).Should().Be(corrected);
    }

    [Theory]
    [InlineData("the the")]
    [InlineData("buy milk")]
    [InlineData("apples, pears and plums")]
    [InlineData("See fig. two and no. three, etc. as needed.")]
    [InlineData("J. smith wrote it.")]
    [InlineData("\"Really?\" she asked.")]
    [InlineData("Wait... then go.")]
    [InlineData("Version 2. iPhone sales rose.")]
    [InlineData("It works. iPhone sales rose.")]
    [InlineData("Go to example.com. https://x.test is next.")]
    [InlineData("a) first choice is here.")]
    public void SentenceCapitalization_ignores_fragments_abbreviations_and_special_tokens(string text)
    {
        Issues(text, GrammarRuleIds.SentenceCapitalization).Should().BeEmpty();
    }

    // ── A / an ───────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("She ate a apple", "a", "an")]
    [InlineData("It took a hour", "a", "an")]
    [InlineData("A honest answer", "A", "An")]
    [InlineData("He is an user", "an", "a")]
    [InlineData("an university campus", "an", "a")]
    [InlineData("An European tour", "An", "A")]
    [InlineData("She saw an cat", "an", "a")]
    [InlineData("a unidentified object", "a", "an")]
    [InlineData("It was an one-off", "an", "a")]
    public void IndefiniteArticle_suggests_the_other_article(string text, string flagged, string replacement)
    {
        var issue = Single(text, GrammarRuleIds.IndefiniteArticle);

        issue.Text.Should().Be(flagged);
        issue.Suggestions.Should().ContainSingle().Which.Replacement.Should().Be(replacement);
    }

    [Theory]
    [InlineData("a cat, an apple, an hour, a user, a university, a European, a one-off, an honest man")]
    [InlineData("an MRI, a UN envoy, an FBI agent, a NASA probe")]
    [InlineData("a historic day and an historic day in a hotel with an herb")]
    [InlineData("from a to b, where a is first and a and b differ")]
    [InlineData("Plan A is ready and vitamin A enters")]
    [InlineData("an x-ray, a b-side, an html page, an sql query")]
    [InlineData("a unidirectional link, a unit, a unique use, a usual utility, an umbrella, an uncle")]
    [InlineData("an 8-bit value, a 5-star hotel, a e.g. placeholder")]
    public void IndefiniteArticle_respects_sounds_acronyms_and_ambiguous_words(string text)
    {
        Issues(text, GrammarRuleIds.IndefiniteArticle).Should().BeEmpty();
    }

    // ── Brackets and quotes ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("An (unclosed aside", 3)]
    [InlineData("An unopened) aside", 11)]
    [InlineData("Mixed (up] here", 6)]
    [InlineData("Nested [a (b] c", 10)]
    public void UnpairedBracket_flags_the_bracket_without_a_partner(string text, int position)
    {
        var issues = Issues(text, GrammarRuleIds.UnpairedBracket);

        issues.Should().Contain(issue => issue.Start == position && issue.Length == 1);
        issues.Should().OnlyContain(issue => issue.Suggestions.Count == 0);
    }

    [Theory]
    [InlineData("Balanced (round [square] brackets) here")]
    [InlineData("a) first, b) second, iii) third")]
    [InlineData("Happy :) sad :( wink ;-) grin =)")]
    [InlineData("Call f(x) and item(s) at step 1) today")]
    public void UnpairedBracket_ignores_pairs_enumerations_emoticons_and_code(string text)
    {
        Issues(text, GrammarRuleIds.UnpairedBracket).Should().BeEmpty();
    }

    [Theory]
    [InlineData("He said \"hello and left", 8)]
    [InlineData("He said \u201Chello and left", 8)]
    [InlineData("He said hello\u201D and left", 13)]
    public void UnpairedQuote_flags_the_quote_without_a_partner(string text, int position)
    {
        var issue = Single(text, GrammarRuleIds.UnpairedQuote);

        issue.Start.Should().Be(position);
        issue.Length.Should().Be(1);
        issue.Suggestions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("He said \"hello\" and \u201Cbye\u201D")]
    [InlineData("\"This quotation continues in the next paragraph.")]
    [InlineData("\u201CThis quotation continues in the next paragraph.")]
    [InlineData("A 5\" screen and a 6\" one, it's Tom's")]
    public void UnpairedQuote_ignores_pairs_continued_quotations_and_inch_marks(string text)
    {
        Issues(text, GrammarRuleIds.UnpairedQuote).Should().BeEmpty();
    }

    // ── Common confusions ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("I could of gone", "could of", "I could have gone")]
    [InlineData("You SHOULD OF seen it", "SHOULD OF", "You SHOULD HAVE seen it")]
    [InlineData("He wouldn't of known", "wouldn't of", "He wouldn't have known")]
    [InlineData("Thanks alot for that", "alot", "Thanks a lot for that")]
    [InlineData("Alot of people came", "Alot", "A lot of people came")]
    [InlineData("Oh, your welcome", "your welcome", "Oh, you're welcome")]
    [InlineData("Your welcome!", "Your welcome", "You're welcome!")]
    public void CommonConfusion_suggests_the_intended_wording(string text, string flagged, string corrected)
    {
        var issue = Single(text, GrammarRuleIds.CommonConfusion);

        issue.Text.Should().Be(flagged);
        Apply(text, issue).Should().Be(corrected);
    }

    [Theory]
    [InlineData("You could of course decline")]
    [InlineData("The might of the empire and a must of the season")]
    [InlineData("Read could_of.txt or visit alot.example.com")]
    [InlineData("We allot time; your welcome mat is here and your welcome was warm")]
    public void CommonConfusion_ignores_legitimate_phrases_and_protected_tokens(string text)
    {
        Issues(text, GrammarRuleIds.CommonConfusion).Should().BeEmpty();
    }

    // ── Options, exclusions, determinism ─────────────────────────────────────────────────────────

    [Fact]
    public void Every_rule_can_be_switched_off_individually()
    {
        const string text = "it broke. the quick  brown fox ate a apple ,then,, (left \"alone and could of stayed.Then";
        var all = GrammarRuleEngine.Check(text);
        var firing = all.Select(issue => issue.RuleId).Distinct().ToArray();
        firing.Should().BeEquivalentTo(
        [
            GrammarRuleIds.SentenceCapitalization,
            GrammarRuleIds.MultipleSpaces,
            GrammarRuleIds.SpaceBeforePunctuation,
            GrammarRuleIds.MissingSpaceAfterPunctuation,
            GrammarRuleIds.IndefiniteArticle,
            GrammarRuleIds.RepeatedPunctuation,
            GrammarRuleIds.UnpairedBracket,
            GrammarRuleIds.UnpairedQuote,
            GrammarRuleIds.CommonConfusion,
        ]);

        foreach (var ruleId in firing)
        {
            var options = GrammarCheckOptions.Default.WithRuleDisabled(ruleId);
            options.IsEnabled(ruleId).Should().BeFalse();
            GrammarRuleEngine.Check(text, options).Select(issue => issue.RuleId)
                .Should().BeEquivalentTo(all.Where(issue => issue.RuleId != ruleId).Select(issue => issue.RuleId));
            options.WithRuleEnabled(ruleId).IsEnabled(ruleId).Should().BeTrue();
        }

        GrammarRuleEngine.Check(text, new GrammarCheckOptions(firing)).Should().BeEmpty();
        GrammarCheckOptions.Default.DisabledRuleIds.Should().BeEmpty();
    }

    [Fact]
    public void Issues_are_ordered_precise_and_deterministic()
    {
        const string text = "She ate a apple ,then left.";

        var first = GrammarRuleEngine.Check(text);
        var second = GrammarRuleEngine.Check(text);

        first.Select(issue => (issue.RuleId, issue.Start, issue.Length))
            .Should().Equal(second.Select(issue => (issue.RuleId, issue.Start, issue.Length)));
        first.Select(issue => issue.Start).Should().BeInAscendingOrder();
        first.Should().OnlyContain(issue => issue.Text == text.Substring(issue.Start, issue.Length));
        first.SelectMany(issue => issue.Suggestions).Should().OnlyContain(suggestion =>
            suggestion.ParagraphOffset >= 0 && suggestion.ParagraphOffset + suggestion.Length <= text.Length);
    }

    [Fact]
    public void Issues_touching_an_excluded_offset_are_dropped()
    {
        const string text = "She ate a apple and a egg";

        var issues = GrammarRuleEngine.Check(text, isExcludedOffset: offset => offset < 15);

        issues.Should().ContainSingle().Which.Start.Should().Be(20);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("en", true)]
    [InlineData("en-US", true)]
    [InlineData("EN-gb", true)]
    [InlineData("en_AU", true)]
    [InlineData("fr-FR", false)]
    [InlineData("de", false)]
    [InlineData("eng-x", false)]
    [InlineData("zxx", false)]
    public void IsEnglish_accepts_english_and_unspecified_languages(string? tag, bool expected)
    {
        GrammarRuleEngine.IsEnglish(tag).Should().Be(expected);
    }

    private static IReadOnlyList<GrammarIssue> Issues(string text, string ruleId) =>
        GrammarRuleEngine.Check(text).Where(issue => issue.RuleId == ruleId).ToArray();

    private static GrammarIssue Single(string text, string ruleId) =>
        Issues(text, ruleId).Should().ContainSingle($"'{text}' has one {ruleId} issue").Which;

    private static string Apply(string text, GrammarIssue issue)
    {
        var suggestion = issue.Suggestions[0];
        return text[..suggestion.ParagraphOffset]
            + suggestion.Replacement
            + text[(suggestion.ParagraphOffset + suggestion.Length)..];
    }
}
