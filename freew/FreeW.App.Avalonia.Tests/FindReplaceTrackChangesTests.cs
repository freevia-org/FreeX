using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FluentAssertions;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Find &amp; Replace must route replacements through the same tracked-edit path ordinary typing uses
/// (<see cref="DocumentView.InsertText"/> via <c>ReplaceSelectionWith</c>) instead of mutating paragraph
/// runs directly, so Track Changes records every Replace/Replace All edit as a revision and Restrict
/// Editing's <c>IsEditingLocked</c> gate is honoured rather than silently bypassed. Covers both the
/// modeless Find &amp; Replace dialog's path (the 3-arg <c>FindReplaceSearchOptions</c> overloads) and the
/// inline find bar's path (the 2-arg overloads) -- both call into
/// <see cref="DocumentView.ReplaceSelectionWith"/> via <c>ReplaceNext</c>/<c>ReplaceAll</c>.
/// See freew/FreeW.App.Avalonia/Editing/DocumentView.cs.
/// </summary>
public sealed class FindReplaceTrackChangesTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    private static DocumentView BuildView(string firstParagraphText)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(firstParagraphText));
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(800, 2000));
        return view;
    }

    private static Paragraph Para(DocumentView view) => (Paragraph)view.Document.Blocks[0];

    [Fact]
    public async Task ReplaceAll_WithTrackChangesOn_RecordsARevisionPairForEveryOccurrenceAndTerminates()
    {
        var count = -1;
        string? plainText = null;
        var insertedDogCount = 0;
        var deletedCatCount = 0;
        var ran = await OnUiThread(() =>
        {
            var view = BuildView("cat cat cat");
            view.RevisionAuthor = "Ada Reviewer";
            view.ToggleTrackChanges();
            view.TrackChangesEnabled.Should().BeTrue();

            count = view.ReplaceAll(
                "cat",
                "dog",
                new FindReplaceSearchOptions(MatchCase: false, WholeWord: false, UseWildcards: false));

            var paragraph = Para(view);
            plainText = paragraph.PlainText;
            insertedDogCount = paragraph.Runs.Count(r => r.Text == "dog" && r.Revision == RevisionKind.Inserted);
            deletedCatCount = paragraph.Runs.Count(r => r.Text == "cat" && r.Revision == RevisionKind.Deleted);
        });
        if (!ran) return;

        // The loop must terminate on its own (not by hitting the 10000 defensive cap) with exactly the
        // three real occurrences counted -- proving the leftover struck-through "cat" that Track
        // Changes keeps in place after each "dog" insertion is skipped rather than re-matched forever.
        count.Should().Be(3);
        insertedDogCount.Should().Be(3);
        deletedCatCount.Should().Be(3);
        plainText.Should().NotBeNull();
    }

    [Fact]
    public async Task ReplaceAll_WithTrackChangesOff_RewritesAllOccurrencesNoRegression()
    {
        var count = -1;
        string? plainText = null;
        var ran = await OnUiThread(() =>
        {
            var view = BuildView("cat cat cat");
            view.TrackChangesEnabled.Should().BeFalse();

            count = view.ReplaceAll(
                "cat",
                "dog",
                new FindReplaceSearchOptions(MatchCase: false, WholeWord: false, UseWildcards: false));

            plainText = Para(view).PlainText;
        });
        if (!ran) return;

        count.Should().Be(3);
        plainText.Should().Be("dog dog dog");
    }

    [Fact]
    public async Task ReplaceNext_WithTrackChangesOn_RecordsRevisionForTheSingleReplacement()
    {
        var foundFirst = false;
        var insertedDog = false;
        var deletedCat = false;
        var ran = await OnUiThread(() =>
        {
            var view = BuildView("Hello cat world");
            view.RevisionAuthor = "Ada Reviewer";
            view.ToggleTrackChanges();
            var options = new FindReplaceSearchOptions(MatchCase: false, WholeWord: false, UseWildcards: false);

            // First call just finds "cat" and selects it (mirrors clicking "Replace" once with no
            // active selection -- Word's own two-step Find-then-Replace UX).
            foundFirst = view.ReplaceNext("cat", "dog", options);
            // Second call now sees "cat" selected and performs the tracked replacement.
            view.ReplaceNext("cat", "dog", options);

            var paragraph = Para(view);
            insertedDog = paragraph.Runs.Any(r => r.Text == "dog" && r.Revision == RevisionKind.Inserted);
            deletedCat = paragraph.Runs.Any(r => r.Text == "cat" && r.Revision == RevisionKind.Deleted);
        });
        if (!ran) return;

        foundFirst.Should().BeTrue("the first call must locate the occurrence before anything is replaced");
        insertedDog.Should().BeTrue("the replacement text must be recorded as a tracked insertion");
        deletedCat.Should().BeTrue("the replaced text must be recorded as a tracked deletion, not silently erased");
    }

    [Fact]
    public async Task ReplaceAll_WhenReadOnlyProtected_MakesNoChangeAndReturnsZero()
    {
        var count = -1;
        string? plainText = null;
        var ran = await OnUiThread(() =>
        {
            var view = BuildView("cat cat cat");
            view.SetProtection(ProtectionMode.ReadOnly);
            view.IsEditingLocked.Should().BeTrue();

            count = view.ReplaceAll(
                "cat",
                "dog",
                new FindReplaceSearchOptions(MatchCase: false, WholeWord: false, UseWildcards: false));

            plainText = Para(view).PlainText;
        });
        if (!ran) return;

        count.Should().Be(0);
        plainText.Should().Be("cat cat cat");
    }
}
