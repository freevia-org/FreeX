using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

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

    [Fact]
    public void ExistingThread_RuntimeControlsExposeAutomationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var existing = new ThreadedComment("Root note", "Quinn")
            {
                Replies = [new CommentReply("Existing reply", "Codex")]
            };
            var dialog = new ThreadedCommentDialog("Sheet1!A1", existing);

            try
            {
                var textBoxes = FindLogicalDescendants<TextBox>(dialog)
                    .ToDictionary(AutomationProperties.GetAutomationId);
                var buttons = FindLogicalDescendants<Button>(dialog)
                    .ToDictionary(AutomationProperties.GetAutomationId);
                var resolvedBox = FindLogicalDescendants<CheckBox>(dialog)
                    .Single(box => AutomationProperties.GetAutomationId(box) == "ThreadedCommentResolvedBox");

                AutomationProperties.GetName(textBoxes["ThreadedCommentRootBox"]).Should().Be("Edit comment");
                AutomationProperties.GetHelpText(textBoxes["ThreadedCommentRootBox"]).Should().Be("Edit the root comment text.");
                AutomationProperties.GetName(textBoxes["ThreadedCommentReplyBox"]).Should().Be("Reply");
                AutomationProperties.GetHelpText(textBoxes["ThreadedCommentReplyBox"]).Should().Be("Enter an optional reply to the threaded comment. Press Ctrl+Enter to reply.");

                buttons["ThreadedCommentReplyButton"].IsDefault.Should().BeTrue();
                AutomationProperties.GetName(buttons["ThreadedCommentReplyButton"]).Should().Be("Reply to comment");
                AutomationProperties.GetHelpText(buttons["ThreadedCommentReplyButton"]).Should().Be("Add a reply to the threaded comment.");
                buttons["ThreadedCommentCancelButton"].IsCancel.Should().BeTrue();
                AutomationProperties.GetName(buttons["ThreadedCommentCancelButton"]).Should().Be("Cancel");

                AutomationProperties.GetName(resolvedBox).Should().Be("Mark as resolved");
                AutomationProperties.GetHelpText(resolvedBox).Should().Be("Mark the threaded comment as resolved.");
            }
            finally
            {
                dialog.Close();
            }
        });
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

    private static IEnumerable<T> FindLogicalDescendants<T>(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match)
                yield return match;

            foreach (var descendant in FindLogicalDescendants<T>(child))
                yield return descendant;
        }
    }
}
