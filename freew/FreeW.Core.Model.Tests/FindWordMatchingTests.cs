namespace FreeW.Core.Model.Tests;

public class FindWordMatchingTests
{
    private static readonly TextSearchOptions Phonetic = new(SoundsLike: true);
    private static readonly TextSearchOptions Forms = new(FindAllWordForms: true);

    private static List<(int Start, int Length)> Find(string haystack, string needle, TextSearchOptions options) =>
        TextSearch.FindAll(haystack, needle, options).ToList();

    [Theory]
    [InlineData("Robert", "R163")]
    [InlineData("Rupert", "R163")]
    [InlineData("Ashcraft", "A261")]
    [InlineData("Tymczak", "T522")]
    [InlineData("Pfister", "P236")]
    [InlineData("Smith", "S530")]
    [InlineData("Lee", "L000")]
    public void Soundex_FollowsTheAmericanRules(string word, string expected)
    {
        FindWordMatching.Soundex(word).Should().Be(expected);
    }

    [Fact]
    public void Soundex_IsNullForNonLatinWords()
    {
        FindWordMatching.Soundex("Привет").Should().BeNull();
        FindWordMatching.Soundex("abc123").Should().BeNull();
    }

    [Fact]
    public void SoundsLike_FindsHomophoneSpellingsAsWholeWords()
    {
        Find("Smith and Smyth met Jones", "Smith", Phonetic)
            .Should().Equal((0, 5), (10, 5));
        // A substring of a longer word is not a phonetic hit.
        Find("Smithson", "Smith", Phonetic).Should().BeEmpty();
    }

    [Fact]
    public void SoundsLike_IsCaseInsensitive_AndMatchesWordsWithTheSameText()
    {
        Find("SMITH smith", "Smith", Phonetic).Should().Equal((0, 5), (6, 5));
        Find("route 66", "66", Phonetic).Should().Equal((6, 2));
    }

    [Fact]
    public void SoundsLike_MultiWordNeedleNeedsConsecutiveWordsOnTheSameLine()
    {
        Find("john smith here", "jon smyth", Phonetic).Should().Equal((0, 10));
        Find("john\rsmith", "jon smyth", Phonetic).Should().BeEmpty();
        Find("john, smith", "jon smyth", Phonetic).Should().BeEmpty();
    }

    [Theory]
    [InlineData("run", "runs")]
    [InlineData("run", "running")]
    [InlineData("run", "ran")]
    [InlineData("run", "runner")]
    [InlineData("make", "making")]
    [InlineData("make", "made")]
    [InlineData("study", "studies")]
    [InlineData("study", "studied")]
    [InlineData("happy", "happier")]
    [InlineData("happy", "happiest")]
    [InlineData("box", "boxes")]
    [InlineData("stop", "stopped")]
    [InlineData("wolf", "wolves")]
    [InlineData("go", "went")]
    [InlineData("is", "were")]
    [InlineData("child", "children")]
    [InlineData("good", "best")]
    [InlineData("dog", "dog's")]
    [InlineData("lie", "lying")]
    [InlineData("walked", "walking")]
    public void AreWordForms_RelatesInflectionsOfOneWord(string a, string b)
    {
        FindWordMatching.AreWordForms(a, b).Should().BeTrue();
        FindWordMatching.AreWordForms(b, a).Should().BeTrue();
    }

    [Theory]
    [InlineData("run", "rung")]
    [InlineData("is", "its")]
    [InlineData("bed", "be")]
    [InlineData("see", "sing")]
    [InlineData("cat", "dog")]
    [InlineData("make", "mask")]
    public void AreWordForms_KeepsUnrelatedWordsApart(string a, string b)
    {
        FindWordMatching.AreWordForms(a, b).Should().BeFalse();
    }

    [Fact]
    public void AllWordForms_ReplacesEveryFormAndLeavesSubstringsAlone()
    {
        const string text = "He runs, ran and is running; a runner; rungs";
        Find(text, "run", Forms).Select(m => text.Substring(m.Start, m.Length))
            .Should().Equal("runs", "ran", "running", "runner");
    }

    [Fact]
    public void WordSearch_IsIgnoredWhenWildcardsAreOn_AndNeedleWithoutWordsMatchesNothing()
    {
        Find("runs", "r*s", new TextSearchOptions(UseWildcards: true, SoundsLike: true)).Should().Equal((0, 4));
        Find("runs", "...", Forms).Should().BeEmpty();
    }
}
