using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ThreadedCommentDialogTests
{
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
                var textBoxes = WpfTestTree.FindLogicalDescendants<TextBox>(dialog)
                    .ToDictionary(AutomationProperties.GetAutomationId);
                var buttons = WpfTestTree.FindLogicalDescendants<Button>(dialog)
                    .ToDictionary(AutomationProperties.GetAutomationId);
                var replySelector = WpfTestTree.FindLogicalDescendants<ComboBox>(dialog)
                    .Single(box => AutomationProperties.GetAutomationId(box) == "ThreadedCommentReplySelector");
                var resolvedBox = WpfTestTree.FindLogicalDescendants<CheckBox>(dialog)
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
                buttons["ThreadedCommentUpdateReplyButton"].IsEnabled.Should().BeTrue();
                AutomationProperties.GetName(buttons["ThreadedCommentDeleteReplyButton"]).Should().Be(UiText.Get("ThreadedComment_DeleteSelectedReplyAutomationName"));
                AutomationProperties.GetHelpText(buttons["ThreadedCommentDeleteReplyButton"]).Should().Be(UiText.Get("ThreadedComment_DeleteSelectedReplyHelpText"));
                buttons["ThreadedCommentDeleteReplyButton"].IsEnabled.Should().BeTrue();
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
    public void ExistingThread_RuntimeReplyActionsDisableForBlankTextOrMissingSelection()
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
                var textBoxes = WpfTestTree.FindLogicalDescendants<TextBox>(dialog)
                    .ToDictionary(AutomationProperties.GetAutomationId);
                var buttons = WpfTestTree.FindLogicalDescendants<Button>(dialog)
                    .ToDictionary(AutomationProperties.GetAutomationId);
                var replySelector = WpfTestTree.FindLogicalDescendants<ComboBox>(dialog)
                    .Single(box => AutomationProperties.GetAutomationId(box) == "ThreadedCommentReplySelector");

                textBoxes["ThreadedCommentSelectedReplyBox"].Text = " ";

                buttons["ThreadedCommentUpdateReplyButton"].IsEnabled.Should().BeFalse();
                buttons["ThreadedCommentDeleteReplyButton"].IsEnabled.Should().BeTrue();

                replySelector.SelectedIndex = -1;

                textBoxes["ThreadedCommentSelectedReplyBox"].Text.Should().BeEmpty();
                buttons["ThreadedCommentUpdateReplyButton"].IsEnabled.Should().BeFalse();
                buttons["ThreadedCommentDeleteReplyButton"].IsEnabled.Should().BeFalse();
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
                var headings = WpfTestTree.FindLogicalDescendants<TextBlock>(dialog)
                    .Select(block => block.Text)
                    .ToList();
                headings.Should().Contain("Quinn - 2026-05-31 08:00 UTC");
                headings.Should().Contain("Codex - 2026-05-31 08:05 UTC");

                var replySelector = WpfTestTree.FindLogicalDescendants<ComboBox>(dialog)
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
}
