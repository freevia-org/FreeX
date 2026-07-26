using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class SpellCheckWorkflowPlannerCustomDictionaryTests
{
    [Fact]
    public void Add_NormalizesSortsAndRejectsCaseInsensitiveDuplicates()
    {
        var words = new List<string> { "zebra", " Apple " };
        var dictionary = SpellCheckWorkflowPlanner.CreateCustomDictionary(words);

        SpellCheckWorkflowPlanner.AddCustomDictionaryWord(words, dictionary, "  banana ").Should().BeTrue();
        SpellCheckWorkflowPlanner.AddCustomDictionaryWord(words, dictionary, "BANANA").Should().BeFalse();

        words.Should().Equal("Apple", "banana", "zebra");
    }

    [Fact]
    public void Remove_IsCaseInsensitiveAndKeepsNormalizedSortedProjection()
    {
        var words = new List<string> { "  zebra", "Apple", "apple", "banana" };

        SpellCheckWorkflowPlanner.RemoveCustomDictionaryWord(words, " APPLE ").Should().BeTrue();
        words.Should().Equal("banana", "zebra");
        SpellCheckWorkflowPlanner.RemoveCustomDictionaryWord(words, "missing").Should().BeFalse();
    }

    [Fact]
    public void RemoveAndSelectNext_SelectsTheNextAvailableWordForRepeatedRemoval()
    {
        var words = new List<string> { "alpha", "bravo", "charlie" };

        SpellCheckWorkflowPlanner.RemoveCustomDictionaryWordAndSelectNext(words, "BRAVO")
            .Should().Be("charlie");
        words.Should().Equal("alpha", "charlie");

        SpellCheckWorkflowPlanner.RemoveCustomDictionaryWordAndSelectNext(words, "charlie")
            .Should().Be("alpha");
        words.Should().Equal("alpha");
    }

    [Fact]
    public void Clear_RemovesEveryPersistedWord()
    {
        var words = new List<string> { "alpha", "beta" };

        SpellCheckWorkflowPlanner.ClearCustomDictionaryWords(words);

        words.Should().BeEmpty();
    }
}
