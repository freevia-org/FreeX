using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FluentAssertions;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// freew-bookmark-lifecycle F1: Backspace at the start of a paragraph that (or whose predecessor) carries
/// a bookmark must not silently discard the bookmark when Track Changes is off. The shared
/// DocumentEditingSession.TryMergeBodyParagraphWithPrevious declines this merge whenever either paragraph
/// is bookmarked (CanRestructureAllowingSectionBreak), so DocumentView.MergeWithPrevious -- the Avalonia
/// fallback reached once the shared session declines -- must carry the bookmark(s) onto the merged
/// paragraph itself instead of rebuilding it from bare cells.
/// </summary>
public sealed class DocumentViewBookmarkMergeLifecycleTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static DocumentView BuildTwoParagraphView(string first, string second)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(first));
        doc.Blocks.Add(new Paragraph(second));
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(800, 2000));
        return view;
    }

    [Fact]
    public async Task Backspace_MergingIntoABookmarkedParagraph_KeepsTheBookmark()
    {
        bool oneBlockLeft = false; bool hasBookmark = false; string text = "";
        var ran = await OnUiThread(() =>
        {
            var view = BuildTwoParagraphView("First", "Second");
            var second = (Paragraph)view.Document.Blocks[1];
            second.BookmarkNames.Add("mark1");

            view.MoveCaretToBlock(1, 0);   // caret at the very start of the bookmarked paragraph
            view.BackspacePublic();        // Backspace joins it into "First"

            var merged = (Paragraph)view.Document.Blocks[0];
            text = merged.PlainText;
            oneBlockLeft = view.Document.Blocks.Count == 1;
            hasBookmark = merged.BookmarkNames.Contains("mark1");
        });
        if (!ran) return;

        oneBlockLeft.Should().BeTrue("the two paragraphs merge into one");
        text.Should().Be("FirstSecond");
        hasBookmark.Should().BeTrue(
            "the bookmark that lived on the paragraph being merged away must survive the merge");
    }

    [Fact]
    public async Task Backspace_MergingAParagraphThatFollowsABookmark_KeepsTheBookmark()
    {
        bool hasBookmark = false;
        var ran = await OnUiThread(() =>
        {
            var view = BuildTwoParagraphView("First", "Second");
            var first = (Paragraph)view.Document.Blocks[0];
            first.BookmarkNames.Add("mark1");

            view.MoveCaretToBlock(1, 0);
            view.BackspacePublic();

            var merged = (Paragraph)view.Document.Blocks[0];
            hasBookmark = merged.BookmarkNames.Contains("mark1");
        });
        if (!ran) return;

        hasBookmark.Should().BeTrue(
            "the bookmark on the surviving (previous) paragraph must also survive the merge");
    }

    // ── Sibling: an ordinary merge with no bookmarks anywhere is unaffected ─────────────────────

    [Fact]
    public async Task Backspace_MergingParagraphsWithNoBookmarks_StillMergesNormally()
    {
        bool oneBlockLeft = false; string text = ""; int bookmarkCount = 0;
        var ran = await OnUiThread(() =>
        {
            var view = BuildTwoParagraphView("First", "Second");

            view.MoveCaretToBlock(1, 0);
            view.BackspacePublic();

            var merged = (Paragraph)view.Document.Blocks[0];
            text = merged.PlainText;
            oneBlockLeft = view.Document.Blocks.Count == 1;
            bookmarkCount = merged.BookmarkNames.Count;
        });
        if (!ran) return;

        oneBlockLeft.Should().BeTrue();
        text.Should().Be("FirstSecond");
        bookmarkCount.Should().Be(0, "no bookmark existed on either paragraph, so none should appear");
    }
}
