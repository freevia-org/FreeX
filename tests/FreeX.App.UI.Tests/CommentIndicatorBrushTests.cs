using System.Windows.Media;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// Tests for the comment-indicator color helper introduced in wave3.
/// Excel parity: Note → red, ThreadedComment → purple #7C379E, Mixed → purple (threaded wins).
/// </summary>
public sealed class CommentIndicatorBrushTests
{
    [Fact]
    public void CommentIndicatorBrush_Note_ReturnsRed()
    {
        var brush = GridView.CommentIndicatorBrush(CellCommentDisplayKind.Note);

        brush.Should().BeSameAs(Brushes.Red, because: "legacy notes must remain red (confirmed Excel parity)");
    }

    [Fact]
    public void CommentIndicatorBrush_ThreadedComment_ReturnsPurple()
    {
        var brush = GridView.CommentIndicatorBrush(CellCommentDisplayKind.ThreadedComment);

        var solidBrush = brush.Should().BeOfType<SolidColorBrush>().Subject;
        solidBrush.Color.R.Should().Be(0x7C, because: "threaded-comment purple R channel is 124 (#7C379E)");
        solidBrush.Color.G.Should().Be(0x37, because: "threaded-comment purple G channel is 55  (#7C379E)");
        solidBrush.Color.B.Should().Be(0x9E, because: "threaded-comment purple B channel is 158 (#7C379E)");
        solidBrush.IsFrozen.Should().BeTrue(because: "brushes must be frozen for WPF render-thread safety and perf");
    }

    [Fact]
    public void CommentIndicatorBrush_Mixed_ReturnsSamePurpleBrushAsThreadedComment()
    {
        // Excel shows the threaded-comment (purple) indicator when both note and threaded comment
        // coexist in the same cell; the legacy red indicator is suppressed.
        var mixedBrush  = GridView.CommentIndicatorBrush(CellCommentDisplayKind.Mixed);
        var threadedBrush = GridView.CommentIndicatorBrush(CellCommentDisplayKind.ThreadedComment);

        mixedBrush.Should().BeSameAs(threadedBrush,
            because: "Mixed uses the same frozen purple brush instance as ThreadedComment");
    }

    [Fact]
    public void CommentIndicatorBrush_ThreadedComment_ReturnsSameFrozenInstanceOnRepeatedCalls()
    {
        // Verifies the brush is cached (not re-allocated on each call).
        var first  = GridView.CommentIndicatorBrush(CellCommentDisplayKind.ThreadedComment);
        var second = GridView.CommentIndicatorBrush(CellCommentDisplayKind.ThreadedComment);

        first.Should().BeSameAs(second,
            because: "the frozen brush must be a static singleton to avoid per-render allocation");
    }
}
