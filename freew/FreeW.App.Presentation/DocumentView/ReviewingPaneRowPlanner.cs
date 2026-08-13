using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>Renderer-neutral text and action metadata for one Reviewing Pane row.</summary>
public sealed record ReviewingPaneRowPlan(
    string AuthorLabel,
    string KindLabel,
    string Title,
    string PreviewText,
    string DateLabel,
    string AcceptToolTip,
    string RejectToolTip);

/// <summary>
/// Applies the WPF Reviewing Pane's row semantics so both renderers show the same author, kind, preview,
/// date, and action wording while remaining free to realize native controls.
/// </summary>
public static class ReviewingPaneRowPlanner
{
    public static ReviewingPaneRowPlan Build(RevisionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var (kindLabel, verb, actionKind) = entry.Kind switch
        {
            RevisionEntryKind.Insertion => ("Insertion", "Inserted", "insertion"),
            RevisionEntryKind.Deletion => ("Deletion", "Deleted", "deletion"),
            _ => ("Formatting", "Formatted", "formatting"),
        };
        var author = string.IsNullOrWhiteSpace(entry.Author) ? "Unknown" : entry.Author;
        var preview = entry.Text.Replace("\r", " ").Replace("\n", " ").Trim();
        var date = FormatDate(entry.DateXml);

        return new ReviewingPaneRowPlan(
            author,
            kindLabel,
            $"{author} • {verb}",
            preview,
            date,
            $"Accept this {actionKind} change",
            $"Reject this {actionKind} change");
    }

    private static string FormatDate(string? dateXml)
    {
        if (string.IsNullOrEmpty(dateXml))
            return string.Empty;

        var timeSeparator = dateXml.IndexOf('T');
        return timeSeparator > 0 ? dateXml[..timeSeparator] : dateXml;
    }
}
