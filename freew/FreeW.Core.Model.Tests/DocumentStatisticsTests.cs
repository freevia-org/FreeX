namespace FreeW.Core.Model.Tests;

public class DocumentStatisticsTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("no terminator here", 1)]              // content but no terminator -> one sentence
    [InlineData("One sentence.", 1)]
    [InlineData("One. Two. Three.", 3)]
    [InlineData("Wait... really?!", 2)]                // ellipsis run + "?!" run = two ends
    [InlineData("Hello!!! World???", 2)]               // each terminator run counts once
    [InlineData("Stop. More words follow", 1)]          // text after the only terminator adds no extra end
    [InlineData("First. Second. Third? Fourth!", 4)]    // mixed terminators, four sentence ends
    public void CountSentences_CountsTerminatorRuns(string? text, int expected)
    {
        DocumentStatistics.CountSentences(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("cat", 1)]
    [InlineData("open", 2)]       // groups o, e -> 2 (no trailing e)
    [InlineData("reaction", 2)]   // vowel groups "ea" and "io" -> 2
    [InlineData("make", 1)]       // silent trailing e discounted: ma(ke) -> 1
    [InlineData("huge", 1)]       // silent trailing e: 1
    [InlineData("reading", 2)]    // rea-ding
    [InlineData("queue", 1)]      // one vowel group "ueue" -> trailing e discounted but floored to 1
    [InlineData("rhythm", 1)]     // 'y' counts as a vowel -> one group
    [InlineData("", 0)]
    [InlineData("123", 0)]        // no letters -> 0 syllables
    public void CountWordSyllables_VowelGroupHeuristic(string word, int expected)
    {
        DocumentStatistics.CountWordSyllables(word).Should().Be(expected);
    }

    [Fact]
    public void CountSyllables_SumsAcrossWords()
    {
        // the(1) cat(1) sat(1) on(1) the(1) mat(1) = 6
        DocumentStatistics.CountSyllables("The cat sat on the mat.").Should().Be(6);
    }

    [Theory]
    [InlineData(1, 1)]      // a single word still reads in one minute
    [InlineData(200, 1)]    // exactly one minute's worth
    [InlineData(201, 2)]    // rounds up
    [InlineData(450, 3)]    // ceil(450/200) = 3
    public void Compute_ReadingTimeRoundsUp(int wordCount, int expectedMinutes)
    {
        var text = string.Join(" ", Enumerable.Repeat("word", wordCount)) + ".";

        var stats = DocumentStatistics.Compute(text);

        stats.Words.Should().Be(wordCount);
        stats.ReadingTimeMinutes.Should().Be(expectedMinutes);
    }

    [Fact]
    public void Compute_EmptyText_ReturnsEmpty()
    {
        DocumentStatistics.Compute((string?)null).Should().Be(DocumentStatistics.Empty);
        DocumentStatistics.Compute("").Should().Be(DocumentStatistics.Empty);
    }

    [Fact]
    public void Compute_EmptyDocument_GuardsDivideByZero()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph()); // one empty paragraph, no words

        var stats = DocumentStatistics.Compute(doc);

        stats.Words.Should().Be(0);
        stats.Sentences.Should().Be(0);
        stats.ReadingTimeMinutes.Should().Be(0);
        stats.AverageWordsPerSentence.Should().Be(0);
        stats.FleschReadingEase.Should().Be(0);
        // Basic counts that still apply are preserved (one paragraph).
        stats.Paragraphs.Should().Be(1);
    }

    [Fact]
    public void Compute_WhitespaceOnlyText_HasNoWordsAndDefaultScore()
    {
        var stats = DocumentStatistics.Compute("   \n  ");

        stats.Words.Should().Be(0);
        stats.FleschReadingEase.Should().Be(0);
        stats.Sentences.Should().Be(0);
    }

    [Fact]
    public void Compute_KnownSample_FleschScoreInExpectedBand()
    {
        // A simple, short sentence scores very high (very easy) on Flesch Reading Ease.
        var stats = DocumentStatistics.Compute("The cat sat on the mat.");

        stats.Words.Should().Be(6);
        stats.Sentences.Should().Be(1);
        stats.Syllables.Should().Be(6);
        stats.AverageWordsPerSentence.Should().Be(6);

        // 206.835 - 1.015*6 - 84.6*(6/6) = 116.145
        stats.FleschReadingEase.Should().BeApproximately(116.145, 0.01);
        stats.FleschReadingEase.Should().BeGreaterThan(90); // "very easy" band
    }

    [Fact]
    public void Compute_DenserProse_ScoresLowerThanSimpleProse()
    {
        var simple = DocumentStatistics.Compute("The cat sat on the mat. The dog ran fast.");
        var dense = DocumentStatistics.Compute(
            "Comprehensive documentation facilitates understanding complicated implementation methodologies effectively.");

        // Longer words / sentences pull the readability score down.
        dense.FleschReadingEase.Should().BeLessThan(simple.FleschReadingEase);
        // A reasonable, sane range for the dense sample (very hard but finite — long multisyllabic
        // words in one long sentence push Flesch well below zero).
        dense.FleschReadingEase.Should().BeInRange(-250, 60);
    }

    [Fact]
    public void Compute_OverDocument_MatchesWordCountBasics()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Hello world. This is a test."));
        doc.Blocks.Add(new Paragraph("Another paragraph here!"));

        var stats = DocumentStatistics.Compute(doc);
        var basic = WordCount.Of(doc);

        // The richer summary reuses the audited basic counts verbatim.
        stats.Words.Should().Be(basic.Words);
        stats.CharactersWithSpaces.Should().Be(basic.CharactersWithSpaces);
        stats.CharactersWithoutSpaces.Should().Be(basic.CharactersWithoutSpaces);
        stats.Paragraphs.Should().Be(basic.Paragraphs);

        // "Hello world." + "This is a test." + "Another paragraph here!" = 3 sentence terminators.
        stats.Sentences.Should().Be(3);
        stats.ReadingTimeMinutes.Should().Be(1);
        stats.AverageWordsPerSentence.Should().BeApproximately(stats.Words / 3.0, 0.0001);
    }
}
