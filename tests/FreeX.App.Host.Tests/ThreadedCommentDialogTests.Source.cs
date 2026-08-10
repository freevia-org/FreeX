using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class ThreadedCommentDialogTests
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
    public void DialogSource_SelectedReplyBox_CommitsEditWithControlEnterWhenActionIsEnabled()
    {
        var source = ReadThreadedCommentDialogSource();

        source.Should().Contain("_selectedReplyBox.PreviewKeyDown +=");
        source.Should().Contain("_updateReplyButton.IsEnabled && Keyboard.Modifiers == ModifierKeys.Control");
        source.Should().Contain("SubmitThreadedCommentReplyEdit(existing);");
        source.Should().Contain("e.Handled = true");
    }

    [Fact]
    public void DialogSource_SelectedReplyActionsTrackSelectionAndText()
    {
        var source = ReadThreadedCommentDialogSource();

        source.Should().Contain("_selectedReplyBox.TextChanged += (_, _) => UpdateSelectedReplyActionState(existing);");
        source.Should().Contain("private void UpdateSelectedReplyActionState(ThreadedComment existing)");
        source.Should().Contain("_deleteReplyButton.IsEnabled = hasSelection;");
        source.Should().Contain("_updateReplyButton.IsEnabled = hasSelection && !string.IsNullOrWhiteSpace(_selectedReplyBox.Text);");
    }

    [Fact]
    public void DialogSource_DelegatesPortableResultPlanningToPresentation()
    {
        var source = ReadThreadedCommentDialogSource();

        source.Should().Contain("ThreadedCommentDialogPlanner.TryCreateResult");
        source.Should().Contain("ThreadedCommentDialogPlanner.TryCreateReplyEditResult");
        source.Should().Contain("ThreadedCommentDialogPlanner.TryCreateReplyDeleteResult");
        source.Should().Contain("ThreadedCommentDialogPlanner.CreateResult");
        source.Should().Contain("ThreadedCommentDialogPlanner.DescribeReply");
        source.Should().Contain("ThreadedCommentDialogPlanner.FormatMessageHeading");
        source.Should().Contain("ThreadedCommentDialogPlanner.ReplySelectorAutomationId");
        source.Should().Contain("ThreadedCommentDialogPlanner.SelectedReplyEditorAutomationId");
        source.Should().Contain("ThreadedCommentDialogPlanner.UpdateReplyAutomationId");
        source.Should().Contain("ThreadedCommentDialogPlanner.DeleteReplyAutomationId");
        source.Should().NotContain("public enum ThreadedCommentDialogAction");
        source.Should().NotContain("public sealed record ThreadedCommentDialogResult");
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

    private static string ReadThreadedCommentDialogSource() =>
        DialogSourceTestSupport.ReadClassSource(
            "ThreadedCommentDialog.cs",
            "public sealed class ThreadedCommentDialog",
            "");

    private static char GetAccessKey(string label)
    {
        var underscoreIndex = label.IndexOf('_', StringComparison.Ordinal);

        underscoreIndex.Should().BeGreaterThanOrEqualTo(0, $"label '{label}' should declare an access key");
        underscoreIndex.Should().BeLessThan(label.Length - 1, $"label '{label}' should include a character after '_'");

        return char.ToUpperInvariant(label[underscoreIndex + 1]);
    }
}
