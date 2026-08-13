namespace FreeP.App.Compositor;

/// <summary>
/// Stable semantic identities shared by the FreeP WPF and Avalonia renderers.
/// </summary>
public static class PresentationSemanticIdentityCatalog
{
    public const string BackstageOverlayAutomationId = "FreePBackstageOverlay";

    public const string BackstageNewBlankPresentationAutomationId =
        "BackstageNewBlankPresentation";

    public const string RichTextEditorInputAutomationId = "FreePRichTextEditorInput";

    public const string CommentsPaneItemAutomationIdPrefix = "FreePCommentsPaneItem";

    public const string CommentsPaneCloseTag = "comments-pane-close";

    public const string CommentMentionSummaryPrefix = "Mentions:";

    public const string CommentMentionTagPrefix = "comment-mention:";

    public const string CommentMentionEditTag = CommentMentionTagPrefix + "edit";

    public const string CommentMentionReplyTag = CommentMentionTagPrefix + "reply";

    public static bool IsCommentMentionSummary(string? text) =>
        text?.StartsWith(CommentMentionSummaryPrefix, StringComparison.Ordinal) == true;

    public static bool IsCommentMentionTag(string? tag) =>
        tag?.StartsWith(CommentMentionTagPrefix, StringComparison.Ordinal) == true;

    public static string BuildCommentMentionCandidateTag(string tag, string insertToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(insertToken);
        return $"{tag}:{insertToken}";
    }
}
