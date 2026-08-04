using FreeW.Core.Model;

namespace FreeW.App.Presentation.QuickParts;

/// <summary>Shared Building Blocks Organizer display, sizing, and selection-state contract.</summary>
public static class BuildingBlocksOrganizerPlanner
{
    public const double Width = 660;
    public const double ListMinWidth = 300;
    public const double ListMinHeight = 240;
    public const double PreviewMinWidth = 300;
    public const double PreviewMinHeight = 240;
    public const double ColumnGap = 12;
    public const string ListLabel = "Building blocks:";
    public const string PreviewLabel = "Preview:";
    public const string EmptyStatus = "No building blocks saved yet. Select some text and choose Save Selection to Quick Parts first.";

    public static string FormatListItem(QuickPart part)
    {
        ArgumentNullException.ThrowIfNull(part);
        return $"{part.Name}  ({part.Gallery} / {part.Category})";
    }

    public static string FormatPreview(QuickPart? part) =>
        part is null
            ? string.Empty
            : string.IsNullOrEmpty(part.Description)
                ? part.Text
                : $"{part.Description}\n\n{part.Text}";

    public static string FormatRemovedStatus(string name) =>
        $"Removed \"{name}\".";
}

/// <summary>A host-neutral list item retaining the full Quick Part metadata for selection.</summary>
public sealed record BuildingBlockListItem(QuickPart Part)
{
    public override string ToString() => BuildingBlocksOrganizerPlanner.FormatListItem(Part);
}
