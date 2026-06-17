namespace FreeW.Core.Model.Tests;

public class CustomDictionaryTests
{
    [Fact]
    public void Add_ThenContains_FindsWord()
    {
        var dictionary = new CustomDictionary();

        dictionary.Add("Kubernetes").Should().BeTrue();

        dictionary.Contains("Kubernetes").Should().BeTrue();
        dictionary.Count.Should().Be(1);
    }

    [Fact]
    public void Contains_IsCaseInsensitive()
    {
        var dictionary = new CustomDictionary();
        dictionary.Add("FreeW");

        dictionary.Contains("freew").Should().BeTrue();
        dictionary.Contains("FREEW").Should().BeTrue();
        dictionary.Contains("FreeW").Should().BeTrue();
    }

    [Fact]
    public void Add_DuplicateCaseInsensitive_DoesNotDuplicate()
    {
        var dictionary = new CustomDictionary();

        dictionary.Add("Foo").Should().BeTrue();
        dictionary.Add("foo").Should().BeFalse();
        dictionary.Add("FOO").Should().BeFalse();

        dictionary.Count.Should().Be(1);
        // The first-added casing is preserved for the persisted/displayed form.
        dictionary.Words.Should().Equal("Foo");
    }

    [Fact]
    public void Add_TrimsSurroundingWhitespace()
    {
        var dictionary = new CustomDictionary();

        dictionary.Add("  spaced  ").Should().BeTrue();

        dictionary.Contains("spaced").Should().BeTrue();
        dictionary.Words.Should().Equal("spaced");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Add_BlankOrNull_IsIgnored(string? word)
    {
        var dictionary = new CustomDictionary();

        dictionary.Add(word!).Should().BeFalse();
        dictionary.Count.Should().Be(0);
    }

    [Fact]
    public void Words_AreSortedCaseInsensitively()
    {
        var dictionary = new CustomDictionary();
        dictionary.Add("zebra");
        dictionary.Add("Apple");
        dictionary.Add("mango");

        dictionary.Words.Should().Equal("Apple", "mango", "zebra");
    }

    [Fact]
    public void Remove_ExistingWord_RemovesCaseInsensitively()
    {
        var dictionary = new CustomDictionary();
        dictionary.Add("Widget");

        dictionary.Remove("widget").Should().BeTrue();

        dictionary.Contains("Widget").Should().BeFalse();
        dictionary.Count.Should().Be(0);
    }

    [Fact]
    public void Remove_MissingWord_ReturnsFalse()
    {
        var dictionary = new CustomDictionary();
        dictionary.Add("Alpha");

        dictionary.Remove("Beta").Should().BeFalse();
        dictionary.Count.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Remove_BlankOrNull_ReturnsFalse(string? word)
    {
        var dictionary = new CustomDictionary();
        dictionary.Add("Alpha");

        dictionary.Remove(word!).Should().BeFalse();
        dictionary.Count.Should().Be(1);
    }

    [Fact]
    public void Contains_BlankOrUnknown_ReturnsFalse()
    {
        var dictionary = new CustomDictionary();
        dictionary.Add("Alpha");

        dictionary.Contains("   ").Should().BeFalse();
        dictionary.Contains("Beta").Should().BeFalse();
    }

    [Fact]
    public void Constructor_SeedsFromWordsDeduplicating()
    {
        var dictionary = new CustomDictionary(["Beta", "alpha", "Alpha", "  ", "beta"]);

        dictionary.Words.Should().Equal("alpha", "Beta");
        dictionary.Count.Should().Be(2);
    }

    [Fact]
    public void Clear_RemovesEveryWord()
    {
        var dictionary = new CustomDictionary(["one", "two", "three"]);

        dictionary.Clear();

        dictionary.Count.Should().Be(0);
        dictionary.Words.Should().BeEmpty();
    }
}
