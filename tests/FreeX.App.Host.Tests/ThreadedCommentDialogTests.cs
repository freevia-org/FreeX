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

        source.Should().Contain("existing is null ? UiText.Get(\"ThreadedComment_AddButton\") : UiText.Get(\"ThreadedComment_ReplyButton\")");
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

        source.Should().Contain("Content = UiText.Get(\"ThreadedComment_CancelButton\")");
        source.Should().Contain("Target = _rootBox");
        source.Should().Contain("Target = _replyBox");
        source.Should().Contain("existing is null ? UiText.Get(\"ThreadedComment_CommentLabel\") : UiText.Get(\"ThreadedComment_EditCommentLabel\")");
        source.Should().Contain("Content = UiText.Get(\"ThreadedComment_ReplyLabel\")");
    }

    [Fact]
    public void DialogSource_AccessKeysAreUniqueWithinNewCommentScope()
    {
        var source = ReadThreadedCommentDialogSource();
        var keys = new[] { "ThreadedComment_CommentLabel", "ThreadedComment_MarkAsResolved", "ThreadedComment_AddButton", "ThreadedComment_CancelButton" };

        source.Should().ContainAll(keys.Select(key => $"UiText.Get(\"{key}\")"));
        keys.Select(key => GetAccessKey(UiText.Get(key))).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void DialogSource_AccessKeysAreUniqueWithinReplyScope()
    {
        var source = ReadThreadedCommentDialogSource();
        var keys = new[]
        {
            "ThreadedComment_EditCommentLabel",
            "ThreadedComment_SelectReplyLabel",
            "ThreadedComment_SelectedReplyTextLabel",
            "ThreadedComment_ReplyLabel",
            "ThreadedComment_MarkAsResolved",
            "ThreadedComment_UpdateReplyButton",
            "ThreadedComment_DeleteReplyButton",
            "ThreadedComment_ReplyButton",
            "ThreadedComment_CancelButton"
        };

        source.Should().ContainAll(keys.Select(key => $"UiText.Get(\"{key}\")"));
        keys.Select(key => GetAccessKey(UiText.Get(key))).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ReplyEditResult_CapturesSelectedReplyIndexAndTrimmedText()
    {
        var existing = new ThreadedComment("Root note", "Quinn")
        {
            Replies =
            [
                new CommentReply("First", "Codex"),
                new CommentReply("Second", "FreeX")
            ]
        };

        ThreadedCommentDialog.TryCreateReplyEditResult(existing, 1, "  Updated second  ", out var result, out var error)
            .Should()
            .BeTrue(error);

        result.Should().Be(new ThreadedCommentDialogResult(
            null,
            null,
            false,
            ThreadedCommentDialogAction.EditReply,
            1,
            "Updated second"));
    }

    [Fact]
    public void ReplyEditResult_CapturesResolvedStateForSelectedReplyAction()
    {
        var existing = new ThreadedComment("Root note", "Quinn")
        {
            Replies = [new CommentReply("First", "Codex")]
        };

        ThreadedCommentDialog.TryCreateReplyEditResult(existing, 0, "Updated", true, out var result, out var error)
            .Should()
            .BeTrue(error);

        result.Should().Be(new ThreadedCommentDialogResult(
            null,
            null,
            true,
            ThreadedCommentDialogAction.EditReply,
            0,
            "Updated"));
    }

    [Fact]
    public void ReplyDeleteResult_CapturesSelectedReplyIndex()
    {
        var existing = new ThreadedComment("Root note", "Quinn")
        {
            Replies = [new CommentReply("First", "Codex")]
        };

        ThreadedCommentDialog.TryCreateReplyDeleteResult(existing, 0, out var result, out var error)
            .Should()
            .BeTrue(error);

        result.Should().Be(new ThreadedCommentDialogResult(
            null,
            null,
            false,
            ThreadedCommentDialogAction.DeleteReply,
            0));
    }

    [Fact]
    public void ReplyDeleteResult_CapturesResolvedStateForSelectedReplyAction()
    {
        var existing = new ThreadedComment("Root note", "Quinn")
        {
            Replies = [new CommentReply("First", "Codex")]
        };

        ThreadedCommentDialog.TryCreateReplyDeleteResult(existing, 0, true, out var result, out var error)
            .Should()
            .BeTrue(error);

        result.Should().Be(new ThreadedCommentDialogResult(
            null,
            null,
            true,
            ThreadedCommentDialogAction.DeleteReply,
            0));
    }

    [Fact]
    public void ReplyEditResult_RejectsBlankReplyText()
    {
        var existing = new ThreadedComment("Root note", "Quinn")
        {
            Replies = [new CommentReply("First", "Codex")]
        };

        ThreadedCommentDialog.TryCreateReplyEditResult(existing, 0, " ", out _, out var error)
            .Should()
            .BeFalse();

        error.Should().Be(UiText.Get("ThreadedComment_EnterReplyMessage"));
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
                var replySelector = FindLogicalDescendants<ComboBox>(dialog)
                    .Single(box => AutomationProperties.GetAutomationId(box) == "ThreadedCommentReplySelector");
                var resolvedBox = FindLogicalDescendants<CheckBox>(dialog)
                    .Single(box => AutomationProperties.GetAutomationId(box) == "ThreadedCommentResolvedBox");

                AutomationProperties.GetName(textBoxes["ThreadedCommentRootBox"]).Should().Be(UiText.Get("ThreadedComment_EditCommentAutomationName"));
                AutomationProperties.GetHelpText(textBoxes["ThreadedCommentRootBox"]).Should().Be(UiText.Get("ThreadedComment_EditCommentHelpText"));
                AutomationProperties.GetName(replySelector).Should().Be(UiText.Get("ThreadedComment_ReplyToEditOrDeleteAutomationName"));
                AutomationProperties.GetHelpText(replySelector).Should().Be(UiText.Get("ThreadedComment_ReplySelectorHelpText"));
                replySelector.SelectedIndex.Should().Be(0);
                AutomationProperties.GetName(textBoxes["ThreadedCommentSelectedReplyBox"]).Should().Be(UiText.Get("ThreadedComment_SelectedReplyTextAutomationName"));
                AutomationProperties.GetHelpText(textBoxes["ThreadedCommentSelectedReplyBox"]).Should().Be(UiText.Get("ThreadedComment_SelectedReplyTextHelpText"));
                textBoxes["ThreadedCommentSelectedReplyBox"].Text.Should().Be("Existing reply");
                AutomationProperties.GetName(textBoxes["ThreadedCommentReplyBox"]).Should().Be(UiText.Get("ThreadedComment_ReplyAutomationName"));
                AutomationProperties.GetHelpText(textBoxes["ThreadedCommentReplyBox"]).Should().Be(UiText.Get("ThreadedComment_ReplyHelpText"));

                AutomationProperties.GetName(buttons["ThreadedCommentUpdateReplyButton"]).Should().Be(UiText.Get("ThreadedComment_UpdateSelectedReplyAutomationName"));
                AutomationProperties.GetHelpText(buttons["ThreadedCommentUpdateReplyButton"]).Should().Be(UiText.Get("ThreadedComment_UpdateSelectedReplyHelpText"));
                AutomationProperties.GetName(buttons["ThreadedCommentDeleteReplyButton"]).Should().Be(UiText.Get("ThreadedComment_DeleteSelectedReplyAutomationName"));
                AutomationProperties.GetHelpText(buttons["ThreadedCommentDeleteReplyButton"]).Should().Be(UiText.Get("ThreadedComment_DeleteSelectedReplyHelpText"));
                buttons["ThreadedCommentReplyButton"].IsDefault.Should().BeTrue();
                AutomationProperties.GetName(buttons["ThreadedCommentReplyButton"]).Should().Be(UiText.Get("ThreadedComment_ReplyToCommentAutomationName"));
                AutomationProperties.GetHelpText(buttons["ThreadedCommentReplyButton"]).Should().Be(UiText.Get("ThreadedComment_ReplyToCommentHelpText"));
                buttons["ThreadedCommentCancelButton"].IsCancel.Should().BeTrue();
                AutomationProperties.GetName(buttons["ThreadedCommentCancelButton"]).Should().Be(UiText.CreateAutomationName(UiText.Cancel));

                AutomationProperties.GetName(resolvedBox).Should().Be(UiText.Get("ThreadedComment_MarkAsResolvedAutomationName"));
                AutomationProperties.GetHelpText(resolvedBox).Should().Be(UiText.Get("ThreadedComment_MarkAsResolvedHelpText"));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ExistingThread_RuntimeConversationHeadingsExposeCreatedTimestamps()
    {
        StaTestRunner.Run(() =>
        {
            var existing = new ThreadedComment("Root note", "Quinn")
            {
                CreatedAtUtc = new DateTimeOffset(2026, 5, 31, 8, 0, 0, TimeSpan.Zero),
                Replies =
                [
                    new CommentReply("Existing reply", "Codex")
                    {
                        CreatedAtUtc = new DateTimeOffset(2026, 5, 31, 8, 5, 0, TimeSpan.Zero)
                    }
                ]
            };
            var dialog = new ThreadedCommentDialog("Sheet1!A1", existing);

            try
            {
                var headings = FindLogicalDescendants<TextBlock>(dialog)
                    .Select(block => block.Text)
                    .ToList();
                headings.Should().Contain("Quinn - 2026-05-31 08:00 UTC");
                headings.Should().Contain("Codex - 2026-05-31 08:05 UTC");

                var replySelector = FindLogicalDescendants<ComboBox>(dialog)
                    .Single(box => AutomationProperties.GetAutomationId(box) == "ThreadedCommentReplySelector");
                var replyItem = replySelector.Items.OfType<ComboBoxItem>().Single();

                replyItem.Content.Should().Be("1. Codex - 2026-05-31 08:05 UTC: Existing reply");
                AutomationProperties.GetName(replyItem)
                    .Should()
                    .Be("Reply 1 by Codex - 2026-05-31 08:05 UTC: Existing reply");
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
