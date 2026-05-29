using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

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

        source.Should().Contain("Content = \"Ca_ncel\"");
        source.Should().Contain("Target = _rootBox");
        source.Should().Contain("Target = _replyBox");
        source.Should().Contain("existing is null ? \"_Comment:\" : \"Edit _comment:\"");
        source.Should().Contain("Content = \"Repl_y:\"");
    }

    [Fact]
    public void DialogSource_AccessKeysAreUniqueWithinNewCommentScope()
    {
        var source = ReadThreadedCommentDialogSource();
        var labels = new[] { "_Comment:", "_Mark as Resolved", "_Add", "Ca_ncel" };

        source.Should().ContainAll(labels.Select(label => $"\"{label}\""));
        labels.Select(GetAccessKey).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void DialogSource_AccessKeysAreUniqueWithinReplyScope()
    {
        var source = ReadThreadedCommentDialogSource();
        var labels = new[] { "Edit _comment:", "Repl_y:", "_Mark as Resolved", "_Reply", "Ca_ncel" };

        source.Should().ContainAll(labels.Select(label => $"\"{label}\""));
        labels.Select(GetAccessKey).Should().OnlyHaveUniqueItems();
    }

    private static string ReadThreadedCommentDialogSource()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ThreadedCommentDialog.cs"));
        var start = source.IndexOf("public sealed class ThreadedCommentDialog", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        return source[start..];
    }

    private static char GetAccessKey(string label)
    {
        var underscoreIndex = label.IndexOf('_', StringComparison.Ordinal);

        underscoreIndex.Should().BeGreaterThanOrEqualTo(0, $"label '{label}' should declare an access key");
        underscoreIndex.Should().BeLessThan(label.Length - 1, $"label '{label}' should include a character after '_'");

        return char.ToUpperInvariant(label[underscoreIndex + 1]);
    }
}
