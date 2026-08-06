using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class CustomDictionaryEditorSessionTests
{
    [Fact]
    public void Constructor_NormalizesWordsAndProjectsInitialAvailability()
    {
        var model = new CustomDictionaryEditorSession([" zebra ", "Apple", "APPLE", ""])
            .Model;

        model.Words.Should().Equal("Apple", "zebra");
        model.SelectedWord.Should().BeNull();
        model.CanAdd.Should().BeFalse();
        model.CanRemove.Should().BeFalse();
        model.CanClear.Should().BeTrue();
    }

    [Fact]
    public void AddPendingWord_NormalizesSortsAndSelectsExistingCaseInsensitiveDuplicate()
    {
        var session = new CustomDictionaryEditorSession(["zebra", "Apple"]);

        var added = session.SetPendingWord(" banana ");
        added.CanAdd.Should().BeTrue();
        added = session.AddPendingWord();
        var duplicate = session.SetPendingWord("BANANA");
        duplicate = session.AddPendingWord();

        added.Words.Should().Equal("Apple", "banana", "zebra");
        added.SelectedWord.Should().Be("banana");
        added.PendingWord.Should().BeNull();
        duplicate.Words.Should().Equal("Apple", "banana", "zebra");
        duplicate.SelectedWord.Should().Be("banana");
    }

    [Fact]
    public void RemoveSelectedWord_ChoosesStableNextItem()
    {
        var session = new CustomDictionaryEditorSession(["alpha", "bravo", "charlie"]);

        var model = session.SelectWord("BRAVO");
        model = session.RemoveSelectedWord();

        model.Words.Should().Equal("alpha", "charlie");
        model.SelectedWord.Should().Be("charlie");
        model.CanRemove.Should().BeTrue();
    }

    [Fact]
    public void Clear_ResetsSelectionAndButtonPolicy()
    {
        var session = new CustomDictionaryEditorSession(["alpha"]);
        session.SelectWord("alpha");

        var model = session.Clear();

        model.Words.Should().BeEmpty();
        model.SelectedWord.Should().BeNull();
        model.CanRemove.Should().BeFalse();
        model.CanClear.Should().BeFalse();
    }
}
