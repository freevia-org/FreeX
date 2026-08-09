using FreeP.App.Localization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record PresentationMediaPlaybackStartOptionPlan(
    MediaPlaybackStartMode Mode,
    string Label);

/// <summary>
/// Localized renderer-neutral text for FreeP workarea panes.
/// </summary>
public static class PresentationPaneTextResources
{
    public static string MediaCaptionsHeading => Loc.Get("Pane_MediaCaptions_Heading");
    public static string PlaybackVolume => Loc.Get("Pane_Media_PlaybackVolume");
    public static string ApplyVolume => Loc.Get("Pane_Media_ApplyVolume");
    public static string PlaybackStart => Loc.Get("Pane_Media_PlaybackStart");
    public static string LoopUntilStopped => Loc.Get("Pane_Media_LoopUntilStopped");
    public static string ShowWhenStopped => Loc.Get("Pane_Media_ShowWhenStopped");
    public static string RewindAfterPlaying => Loc.Get("Pane_Media_RewindAfterPlaying");
    public static string PlayFullScreen => Loc.Get("Pane_Media_PlayFullScreen");
    public static string StopAfterSlides => Loc.Get("Pane_Media_StopAfterSlides");
    public static string ApplyPlayback => Loc.Get("Pane_Media_ApplyPlayback");
    public static string TrimStartMilliseconds => Loc.Get("Pane_Media_TrimStartMilliseconds");
    public static string TrimEndMilliseconds => Loc.Get("Pane_Media_TrimEndMilliseconds");
    public static string FadeInMilliseconds => Loc.Get("Pane_Media_FadeInMilliseconds");
    public static string FadeOutMilliseconds => Loc.Get("Pane_Media_FadeOutMilliseconds");
    public static string ApplyTiming => Loc.Get("Pane_Media_ApplyTiming");
    public static string MediaBookmarks => Loc.Get("Pane_Media_Bookmarks");
    public static string BookmarkName => Loc.Get("Pane_Media_BookmarkName");
    public static string BookmarkTimeMilliseconds => Loc.Get("Pane_Media_BookmarkTimeMilliseconds");
    public static string AddBookmark => Loc.Get("Pane_Media_AddBookmark");
    public static string ReplaceBookmark => Loc.Get("Pane_Media_ReplaceBookmark");
    public static string DeleteBookmark => Loc.Get("Pane_Media_DeleteBookmark");
    public static string AltTextHeading => Loc.Get("Pane_AltText_Heading");
    public static string ReadingOrderHeading => Loc.Get("Pane_ReadingOrder_Heading");
    public static string ReadingOrderSelectedItem => Loc.Get("Pane_ReadingOrder_SelectedItem");
    public static string ProofingHeading => Loc.Get("Pane_Proofing_Heading");
    public static string ProofingSelectedIssue => Loc.Get("Pane_Proofing_SelectedIssue");
    public static string NewCommentDefault => Loc.Get("Pane_Comments_NewCommentDefault");
    public static string NewReplyDefault => Loc.Get("Pane_Comments_NewReplyDefault");
    public static string NewCommentCommand => Loc.Get("Pane_Comments_NewCommentCommand");
    public static string ReplyCommand => Loc.Get("Pane_Comments_ReplyCommand");

    public static IReadOnlyList<PresentationMediaPlaybackStartOptionPlan> MediaPlaybackStartOptions =>
    [
        new(MediaPlaybackStartMode.InClickSequence, Loc.Get("Pane_Media_StartOnClick")),
        new(MediaPlaybackStartMode.Automatically, Loc.Get("Pane_Media_StartAutomatically")),
    ];

    public static string BuildMediaCaptionsHeading(string? shapeName) =>
        string.IsNullOrWhiteSpace(shapeName)
            ? MediaCaptionsHeading
            : Loc.Format("Pane_MediaCaptions_HeadingFormat", shapeName);

    public static string BuildAltTextHeading(string? shapeName) =>
        string.IsNullOrWhiteSpace(shapeName)
            ? AltTextHeading
            : Loc.Format("Pane_AltText_HeadingFormat", shapeName);

    public static string BuildReadingOrderHeading(int slideIndex, int itemCount) =>
        Loc.Format("Pane_ReadingOrder_HeadingFormat", slideIndex + 1, itemCount);

    public static string BuildReadingOrderSelectedMessage(string shapeName) =>
        Loc.Format("Pane_ReadingOrder_SelectedFormat", shapeName);

    public static string BuildReadingOrderItemTitle(int readingOrderIndex, string shapeName) =>
        Loc.Format("Pane_ReadingOrder_ItemTitleFormat", readingOrderIndex + 1, shapeName);

    public static string BuildReadingOrderItemMetadata(string shapeTypeLabel, int nestingDepth) =>
        Loc.Format("Pane_ReadingOrder_ItemMetadataFormat", shapeTypeLabel, nestingDepth);

    public static string BuildReadingOrderSelectToolTip(string shapeName) =>
        Loc.Format("Pane_ReadingOrder_SelectToolTipFormat", shapeName);

    public static string BuildProofingHeading(int issueCount) =>
        Loc.Format("Pane_Proofing_HeadingFormat", issueCount);

    public static string BuildProofingSelectedMessage(
        string slideDisplay,
        string text,
        string suggestedReplacement) =>
        Loc.Format("Pane_Proofing_SelectedFormat", slideDisplay, text, suggestedReplacement);
}
