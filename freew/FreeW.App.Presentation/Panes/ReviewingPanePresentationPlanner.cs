using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Panes;

public enum ReviewingPanePresentationProfile
{
    CompactWpf,
    DetailedAvalonia
}

public sealed record ReviewingPaneSortOptionDescriptor(
    ReviewRevisionSortOrder Order,
    string Label);

public sealed record ReviewingPaneActionDescriptor(
    string Label,
    string ToolTip);

public sealed record ReviewingPaneActionDescriptors(
    ReviewingPaneActionDescriptor AcceptSelected,
    ReviewingPaneActionDescriptor RejectSelected,
    ReviewingPaneActionDescriptor Previous,
    ReviewingPaneActionDescriptor Next,
    ReviewingPaneActionDescriptor AcceptAll,
    ReviewingPaneActionDescriptor RejectAll);

public sealed record ReviewingPanePresentationDescriptor(
    string PaneTitle,
    string SortLabel,
    IReadOnlyList<ReviewingPaneSortOptionDescriptor> SortOptions,
    ReviewingPaneActionDescriptors Actions);

public sealed record ReviewingPaneRevisionPresentation(
    string KindLabel,
    string AuthorText,
    string CaptionText,
    string SnippetText,
    string DateText,
    string AcceptToolTip,
    string RejectToolTip);

/// <summary>
/// Owns reviewing-pane wording and renderer-profile row projection. Renderers retain native controls,
/// layout, brushes, focus, and command dispatch.
/// </summary>
public static class ReviewingPanePresentationPlanner
{
    private static readonly IReadOnlyList<ReviewingPaneSortOptionDescriptor> SortOptions =
    [
        new(ReviewRevisionSortOrder.Sequence, "By Sequence"),
        new(ReviewRevisionSortOrder.Author, "By Author"),
        new(ReviewRevisionSortOrder.Kind, "By Type"),
        new(ReviewRevisionSortOrder.Date, "By Date")
    ];

    private static readonly ReviewingPanePresentationDescriptor CompactWpf = new(
        PaneTitle: "Revisions",
        SortLabel: "Sort:",
        SortOptions,
        new ReviewingPaneActionDescriptors(
            new("Accept", "Accept the selected change"),
            new("Reject", "Reject the selected change"),
            new("\u25B2", "Previous change (jump up)"),
            new("\u25BC", "Next change (jump down)"),
            new("Accept All", "Accept all tracked changes"),
            new("Reject All", "Reject all tracked changes")));

    private static readonly ReviewingPanePresentationDescriptor DetailedAvalonia = new(
        PaneTitle: "Tracked Changes",
        SortLabel: "Sort:",
        SortOptions,
        new ReviewingPaneActionDescriptors(
            new("Accept", "Accept the selected change"),
            new("Reject", "Reject the selected change"),
            new("Previous", "Previous change (jump up)"),
            new("Next", "Next change (jump down)"),
            new("Accept All", "Accept all tracked changes"),
            new("Reject All", "Reject all tracked changes")));

    public static ReviewingPanePresentationDescriptor For(ReviewingPanePresentationProfile profile) =>
        profile switch
        {
            ReviewingPanePresentationProfile.CompactWpf => CompactWpf,
            ReviewingPanePresentationProfile.DetailedAvalonia => DetailedAvalonia,
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
        };

    public static string BuildCountText(int count, ReviewingPanePresentationProfile profile)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return profile switch
        {
            ReviewingPanePresentationProfile.CompactWpf => count switch
            {
                0 => "No tracked changes",
                1 => "1 change",
                _ => $"{count} changes"
            },
            ReviewingPanePresentationProfile.DetailedAvalonia => count switch
            {
                0 => "No tracked changes",
                1 => "1 tracked change",
                _ => $"{count} tracked changes"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
        };
    }

    public static ReviewingPaneRevisionPresentation BuildRevision(
        RevisionEntry entry,
        ReviewingPanePresentationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return profile switch
        {
            ReviewingPanePresentationProfile.CompactWpf => BuildCompactRevision(entry),
            ReviewingPanePresentationProfile.DetailedAvalonia => BuildDetailedRevision(entry),
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
        };
    }

    private static ReviewingPaneRevisionPresentation BuildCompactRevision(RevisionEntry entry)
    {
        var kindLabel = entry.Kind switch
        {
            RevisionEntryKind.Insertion => "Inserted",
            RevisionEntryKind.Deletion => "Deleted",
            RevisionEntryKind.Formatting => "Formatted",
            _ => entry.Kind.ToString()
        };
        var author = string.IsNullOrWhiteSpace(entry.Author) ? "Unknown" : entry.Author;
        var snippet = entry.Text.Replace("\r", " ").Replace("\n", " ").Trim();
        return new ReviewingPaneRevisionPresentation(
            kindLabel,
            author,
            $"{author} \u2022 {kindLabel}",
            snippet,
            FormatDate(entry.DateXml),
            "Accept the selected change",
            "Reject the selected change");
    }

    private static ReviewingPaneRevisionPresentation BuildDetailedRevision(RevisionEntry entry)
    {
        var kindLabel = entry.Kind switch
        {
            RevisionEntryKind.Insertion => "Insertion",
            RevisionEntryKind.Deletion => "Deletion",
            RevisionEntryKind.Formatting => "Formatting",
            _ => entry.Kind.ToString()
        };
        var author = string.IsNullOrWhiteSpace(entry.Author) ? "(unknown)" : entry.Author;
        var snippet = entry.Text.Length > 60
            ? string.Concat("\"", entry.Text.AsSpan(0, 57), "\u2026\"")
            : $"\"{entry.Text}\"";
        var actionKind = kindLabel.ToLowerInvariant();
        return new ReviewingPaneRevisionPresentation(
            kindLabel,
            author,
            $"{author} \u2022 {kindLabel}",
            snippet,
            FormatDate(entry.DateXml),
            $"Accept this {actionKind} change",
            $"Reject this {actionKind} change");
    }

    private static string FormatDate(string? dateXml)
    {
        if (string.IsNullOrEmpty(dateXml))
            return string.Empty;

        var separator = dateXml.IndexOf('T');
        return separator > 0 ? dateXml[..separator] : dateXml;
    }
}
