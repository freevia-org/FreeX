using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Word-style Display for Review modes used by both FreeW renderers.
/// </summary>
public enum ReviewDisplayMode
{
    AllMarkup,
    SimpleMarkup,
    NoMarkup,
    Original
}

/// <summary>
/// Renderer-neutral view policy for Review > Tracking display controls.
/// </summary>
public readonly record struct ReviewDisplayPolicy(
    ReviewDisplayMode DisplayMode,
    bool ShowInsertionsAndDeletions = true,
    bool ShowComments = true,
    bool ShowFormatting = true)
{
    public static ReviewDisplayPolicy Default { get; } = new(ReviewDisplayMode.AllMarkup);

    public bool ShouldShowSimpleMarkupChangeBar => DisplayMode == ReviewDisplayMode.SimpleMarkup;

    /// <summary>
    /// Whether changed paragraphs receive Word-style margin bars. Word keeps these cues visible
    /// in both All Markup and Simple Markup; hiding insertion/deletion markup suppresses them.
    /// </summary>
    public bool ShouldShowRevisionMarginBar =>
        ShowInsertionsAndDeletions
        && DisplayMode is ReviewDisplayMode.AllMarkup or ReviewDisplayMode.SimpleMarkup;

    public bool ShouldHighlightComments =>
        ShowComments && DisplayMode is ReviewDisplayMode.AllMarkup or ReviewDisplayMode.SimpleMarkup;

    public bool ShouldHighlightFormattingChanges =>
        ShowFormatting && DisplayMode == ReviewDisplayMode.AllMarkup;

    public bool IsRevisionTextVisible(RevisionKind revision) =>
        RevisionDecision(revision).IsTextVisible;

    public bool ShouldApplyRevisionStyling(RevisionKind revision) =>
        RevisionDecision(revision).IsRevisionStylingApplied;

    public ReviewRevisionDisplayDecision RevisionDecision(RevisionKind revision)
    {
        var textVisible = revision switch
        {
            RevisionKind.Inserted => DisplayMode != ReviewDisplayMode.Original,
            RevisionKind.Deleted => DisplayMode is ReviewDisplayMode.AllMarkup or ReviewDisplayMode.Original,
            _ => true
        };

        var styled = revision != RevisionKind.None
            && textVisible
            && ShowInsertionsAndDeletions
            && DisplayMode == ReviewDisplayMode.AllMarkup;

        return new ReviewRevisionDisplayDecision(
            revision,
            textVisible,
            styled,
            styled && revision == RevisionKind.Inserted,
            styled && revision == RevisionKind.Deleted);
    }
}

public readonly record struct ReviewRevisionDisplayDecision(
    RevisionKind Revision,
    bool IsTextVisible,
    bool IsRevisionStylingApplied,
    bool IsInsertionDecorationApplied,
    bool IsDeletionDecorationApplied)
{
    public bool IsHiddenButPreserved => Revision != RevisionKind.None && !IsTextVisible;
}
