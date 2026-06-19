namespace FreeW.Core.Model.Tests;

public class HyphenatorTests
{
    // Render the break points as a dashed word ("hy-phen-ation") so the expected splits are readable.
    private static string Dashed(string word)
    {
        var points = Hyphenator.BreakPoints(word);
        if (points.Count == 0)
            return word;
        var sb = new System.Text.StringBuilder();
        var next = 0;
        for (var i = 0; i < word.Length; i++)
        {
            if (next < points.Count && points[next] == i)
            {
                sb.Append('-');
                next++;
            }
            sb.Append(word[i]);
        }
        return sb.ToString();
    }

    [Theory]
    [InlineData("a")]
    [InlineData("to")]
    [InlineData("cat")]
    [InlineData("four")] // 4 letters: below the 5-letter minimum
    public void ShortWords_AreNotHyphenated(string word)
    {
        Hyphenator.BreakPoints(word).Should().BeEmpty();
        Hyphenator.Hyphenate(word).Should().Be(word);
    }

    [Theory]
    [InlineData("don't")]
    [InlineData("co-op")]
    [InlineData("file2")]
    [InlineData("3.14159")]
    public void NonAlphabeticTokens_AreLeftWhole(string word)
    {
        Hyphenator.BreakPoints(word).Should().BeEmpty();
    }

    [Fact]
    public void DoubledConsonant_SplitsBetweenThePair()
    {
        // rab-bit, let-ter: the doubled consonant is split.
        Dashed("rabbit").Should().Be("rab-bit");
        Dashed("letter").Should().Be("let-ter");
    }

    [Fact]
    public void OpenSyllable_BreaksBeforeASingleConsonant()
    {
        // V C V -> break before the consonant: ba-sic, ho-tel.
        Dashed("basic").Should().Be("ba-sic");
        Dashed("hotel").Should().Be("ho-tel");
    }

    [Fact]
    public void ConsonantCluster_SplitsInsideTheCluster()
    {
        // "monster": the "nst" cluster is split (a break lands inside it) rather than left whole.
        Dashed("monster").Should().Be("mons-ter");
    }

    [Fact]
    public void Digraph_IsNotSplitInternally()
    {
        // The "th" digraph is never broken between its two letters; the break lands before it.
        var dashed = Dashed("mother");
        dashed.Should().NotContain("t-h");
        dashed.Should().Contain("-th");
    }

    [Fact]
    public void CommonWords_BreakAtStableHeuristicPoints()
    {
        // The feature's namesake plus a frequent word. Exact splits are heuristic but deterministic; assert
        // the stable observed output so a regression in the algorithm is caught.
        Dashed("hyphenation").Should().Be("hy-phe-na-tion");
        Dashed("computer").Should().Be("com-pu-ter");
    }

    [Fact]
    public void BreakPoints_NeverLeaveFewerThanTwoLettersAtEitherEnd()
    {
        foreach (var word in new[] { "hyphenation", "computer", "rabbit", "monster", "wonderful", "establishment" })
        {
            var points = Hyphenator.BreakPoints(word);
            foreach (var p in points)
            {
                p.Should().BeGreaterThanOrEqualTo(2);
                p.Should().BeLessThanOrEqualTo(word.Length - 2);
            }
            // Strictly increasing, no two breaks adjacent (no single-letter middle fragment).
            for (var i = 1; i < points.Count; i++)
                (points[i] - points[i - 1]).Should().BeGreaterThanOrEqualTo(2);
        }
    }

    [Fact]
    public void Hyphenate_InsertsSoftHyphensAtBreakPoints()
    {
        var result = Hyphenator.Hyphenate("rabbit");
        result.Should().Be("rab" + Hyphenator.SoftHyphen + "bit");
        // Stripping the soft hyphens recovers the original word.
        result.Replace(Hyphenator.SoftHyphen.ToString(), string.Empty).Should().Be("rabbit");
    }

    [Fact]
    public void HyphenateText_HyphenatesEachWord_PreservingWhitespaceAndPunctuation()
    {
        var result = Hyphenator.HyphenateText("the rabbit ran");
        // Only "rabbit" is long enough to break; the short words and the spaces are untouched.
        result.Should().Be("the rab" + Hyphenator.SoftHyphen + "bit ran");
    }

    [Fact]
    public void HyphenateText_PreservesTrailingPunctuationOnAToken()
    {
        var result = Hyphenator.HyphenateText("(rabbit),");
        result.Replace(Hyphenator.SoftHyphen.ToString(), string.Empty).Should().Be("(rabbit),");
        result.Should().Contain(Hyphenator.SoftHyphen.ToString());
    }

    [Fact]
    public void HyphenateText_EmptyOrShort_ReturnsUnchanged()
    {
        Hyphenator.HyphenateText("").Should().Be("");
        Hyphenator.HyphenateText("a b c").Should().Be("a b c");
    }
}
