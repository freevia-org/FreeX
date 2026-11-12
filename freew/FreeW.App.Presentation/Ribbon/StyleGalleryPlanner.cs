using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>A custom style offered by the Home &gt; Styles gallery.</summary>
public sealed record StyleGalleryEntry(string Id, string Name, StyleType Type);

/// <summary>The commands on a gallery swatch's right-click menu (Word's Styles gallery menu).</summary>
public enum StyleGalleryContextCommand
{
    UpdateToMatchSelection,
    Modify,
    RemoveFromGallery,
    Delete,
}

public sealed record StyleGalleryContextMenuItem(
    StyleGalleryContextCommand Command,
    string Label,
    string AutomationId,
    bool IsEnabled);

/// <summary>
/// Decides which custom styles the Styles gallery lists and what a swatch's context menu offers, so the
/// renderers do not each re-derive Word's rules: a custom paragraph or character style is listed when it
/// is flagged "Add to the Styles gallery"; the character half of a linked pair never is (the pair is one
/// style, shown under the paragraph half's name).
/// </summary>
public static class StyleGalleryPlanner
{
    public static IReadOnlyList<StyleGalleryEntry> CustomEntries(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Styles.Values
            .Where(style => style.Type is StyleType.Paragraph or StyleType.Character
                && style.QuickFormat
                && !StyleManager.IsBuiltIn(style.Id)
                && !StyleManager.IsLinkedCharacterHalf(document, style))
            .Select(style => new StyleGalleryEntry(style.Id, style.Name, style.Type))
            .ToArray();
    }

    /// <summary>
    /// A value that changes whenever the gallery's custom entries (or how a swatch should render)
    /// change, so a renderer can cheaply tell that its swatches are stale.
    /// </summary>
    public static string Signature(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return string.Join(
            "\u001f",
            document.Styles.Values.Select(style => string.Join(
                "\u001e",
                style.Id,
                style.Name,
                style.Type,
                style.QuickFormat,
                style.BasedOnStyleId,
                style.LinkedStyleId,
                style.Run.GetHashCode())));
    }

    public static IReadOnlyList<StyleGalleryContextMenuItem> BuildContextMenu(TextDocument document, string styleId)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrEmpty(styleId) || !document.Styles.TryGetValue(styleId, out var style))
            return [];

        var custom = !StyleManager.IsBuiltIn(styleId);
        return
        [
            new(
                StyleGalleryContextCommand.UpdateToMatchSelection,
                StyleDefinitionDialogText.GalleryUpdateToMatch(style.Name),
                "StylesGalleryUpdateToMatchMenuItem",
                IsEnabled: true),
            new(
                StyleGalleryContextCommand.Modify,
                StyleDefinitionDialogText.GalleryModify,
                "StylesGalleryModifyMenuItem",
                IsEnabled: true),
            new(
                StyleGalleryContextCommand.RemoveFromGallery,
                StyleDefinitionDialogText.GalleryRemove,
                "StylesGalleryRemoveMenuItem",
                // The built-in swatches are a fixed part of the gallery.
                IsEnabled: custom),
            new(
                StyleGalleryContextCommand.Delete,
                StyleDefinitionDialogText.GalleryDelete(style.Name),
                "StylesGalleryDeleteMenuItem",
                // Built-in styles cannot be deleted (StyleManager refuses them).
                IsEnabled: custom),
        ];
    }
}
