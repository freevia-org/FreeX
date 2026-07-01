using System.IO;
using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Presentation.Comments;
using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ObjectDialogTests
{
    [Fact]
    public void TextEntryDialog_CreateResult_TrimsNullToEmptyText()
    {
        TextEntryDialog.CreateResult(null).Text.Should().Be("");
        TextEntryDialog.CreateResult("  keep spacing inside  ").Text.Should().Be("keep spacing inside");

        var source = ReadObjectDialogSources();
        source.Should().Contain("TextEntryDialogPlanner.CreateResult(text)");
        source.Should().NotContain("public sealed record TextEntryDialogResult");
    }

    [Fact]
    public void ThreadedCommentDialog_CreateResult_DistinguishesRootEditFromReply()
    {
        var existing = new ThreadedComment("Old root", "Quinn")
        {
            Replies = [new CommentReply("Existing reply", "Codex")]
        };

        ThreadedCommentDialog.CreateResult(null, "  New root  ", "", isResolved: false)
            .Should()
            .Be(new ThreadedCommentDialogResult(null, "New root", false));
        ThreadedCommentDialog.CreateResult(existing, "  Edited root  ", "  Reply text  ", isResolved: true)
            .Should()
            .Be(new ThreadedCommentDialogResult("Edited root", "Reply text", true));
        ThreadedCommentDialog.CreateResult(existing, " Old root ", " ", isResolved: false)
            .Should()
            .Be(new ThreadedCommentDialogResult(null, null, false));
    }

    [Fact]
    public void ThreadedCommentDialog_TryCreateResult_RejectsBlankNewComment()
    {
        ThreadedCommentDialog.TryCreateResult(null, " ", "", isResolved: false, out _, out var error)
            .Should()
            .BeFalse();

        error.Should().Be(UiText.Get("ThreadedComment_EnterCommentMessage"));
    }

    [Fact]
    public void ThreadedCommentDialog_TryCreateResult_AllowsBlankReplyWhenResolvingExistingThread()
    {
        var existing = new ThreadedComment("Old root", "Quinn");

        ThreadedCommentDialog.TryCreateResult(existing, " Old root ", " ", isResolved: true, out var result, out var error)
            .Should()
            .BeTrue(error);

        result.Should().Be(new ThreadedCommentDialogResult(null, null, true));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ThreadedCommentDialog_TryCreateResult_RejectsBlankExistingRootEdit(string rootText)
    {
        var existing = new ThreadedComment("Old root", "Quinn");

        ThreadedCommentDialog.TryCreateResult(existing, rootText, "Reply", isResolved: false, out _, out var error)
            .Should()
            .BeFalse();

        error.Should().Be(UiText.Get("ThreadedComment_EnterCommentMessage"));
    }

    [Fact]
    public void ThreadedCommentDialog_BlankNewCommentWarnsAndRefocusesCommentBox()
    {
        var source = ReadClassSource("ThreadedCommentDialog.cs", "public sealed class ThreadedCommentDialog", "");

        source.Should().Contain("if (!TryCreateResult(existing, _rootBox.Text, _replyBox.Text, _resolveBox.IsChecked == true, out var result, out var error))");
        source.Should().Contain("ShowInvalidThreadedCommentWarning(error ?? UiText.Get(\"ThreadedComment_EnterCommentMessage\"), _rootBox);");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this, message, Title, target);");
    }

    [Fact]
    public void TextEntryDialogOpenedFromKeyboard_FocusesTextBox()
    {
        var source = ReadClassSource("TextEntryDialogs.cs", "public class TextEntryDialog", "");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_textBox);");
    }
}
