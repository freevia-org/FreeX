using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class ThreadedCommentDialogTests
{
    [Fact]
    public void DialogSource_ExistingThread_UsesReplyAccessKeyInsteadOfGenericOk()
    {
        var source = ReadThreadedCommentDialogSource();

        source.Should().Contain("existing is null ? \"_Add\" : \"_Reply\"");
        source.Should().Contain("IsDefault = true");
    }

    [Fact]
    public void DialogSource_ReplyBox_CommitsWithControlEnter()
    {
        var source = ReadThreadedCommentDialogSource();

        source.Should().Contain("_replyBox.PreviewKeyDown +=");
        source.Should().Contain("Keyboard.Modifiers == ModifierKeys.Control");
        source.Should().Contain("e.Key == Key.Enter");
        source.Should().Contain("SubmitThreadedCommentDialog(existing);");
        source.Should().Contain("e.Handled = true");
    }

    [Fact]
    public void DialogSource_ReplyBox_KeepsAcceptsReturnForPlainEnter()
    {
        var source = ReadThreadedCommentDialogSource();

        source.Should().Contain("private readonly TextBox _replyBox = new() { AcceptsReturn = true");
    }

    [Fact]
    public void DialogSource_ExistingThread_FocusesReplyBoxOnOpen()
    {
        var source = ReadThreadedCommentDialogSource();

        source.Should().Contain("var target = existing is null ? _rootBox : _replyBox;");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void DialogSource_CommentAndReplyLabelsTargetEntryBoxesAndCancelHasAccessKey()
    {
        var source = ReadThreadedCommentDialogSource();

        source.Should().Contain("Content = \"_Cancel\"");
        source.Should().Contain("Target = _rootBox");
        source.Should().Contain("Target = _replyBox");
        source.Should().Contain("existing is null ? \"_Comment:\" : \"Edit _comment:\"");
        source.Should().Contain("Content = \"_Reply:\"");
    }

    private static string ReadThreadedCommentDialogSource()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ObjectDialogs.cs"));
        var start = source.IndexOf("public sealed class ThreadedCommentDialog", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        return source[start..];
    }
}
