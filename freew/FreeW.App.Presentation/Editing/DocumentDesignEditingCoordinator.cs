using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

/// <summary>Reports the shared undo entry created by a portable document-design mutation.</summary>
public readonly record struct DocumentDesignEditResult(bool Applied, string Label);

/// <summary>
/// Owns portable document properties, page-surface, page-setup, and Design-catalog command composition.
/// Renderers retain native edit commits, dialogs, projection, focus, and invalidation.
/// </summary>
public sealed class DocumentDesignEditingCoordinator
{
    private readonly DocumentEditingSession _session;

    internal DocumentDesignEditingCoordinator(DocumentEditingSession session) => _session = session;

    public DocumentDesignEditResult ApplyDocumentProperties(DocumentPropertiesDialogValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Execute(new ApplyDocumentPropertiesCommand(values));
    }

    public DocumentDesignEditResult UpdatePage(
        Action<PageSettings> mutation,
        string label = "Page Setup")
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        var settings = _session.Document.Page.Clone();
        mutation(settings);
        return SetPageSettings(settings, label);
    }

    public DocumentDesignEditResult SetPageSettings(
        PageSettings settings,
        string label = "Page Setup")
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return Execute(new SetPageSettingsCommand(settings.Clone(), label));
    }

    public DocumentDesignEditResult ApplyTheme(DocumentTheme theme) =>
        ApplyCatalog("Apply Theme", theme, static (document, value) => DocumentTheme.Apply(document, value));

    public DocumentDesignEditResult ApplyThemeColors(DocumentTheme theme) =>
        ApplyCatalog("Theme Colors", theme, static (document, value) => DocumentTheme.ApplyColors(document, value));

    public DocumentDesignEditResult ApplyStyleSet(DocumentStyleSet styleSet) =>
        ApplyCatalog("Style Set", styleSet, static (document, value) => DocumentStyleSet.Apply(document, value));

    public DocumentDesignEditResult ApplyFontSet(DocumentFontSet fontSet) =>
        ApplyCatalog("Theme Fonts", fontSet, static (document, value) => DocumentFontSet.Apply(document, value));

    public DocumentDesignEditResult ApplyParagraphSpacingSet(DocumentParagraphSpacingSet spacingSet) =>
        ApplyCatalog(
            "Paragraph Spacing",
            spacingSet,
            static (document, value) => DocumentParagraphSpacingSet.Apply(document, value));

    public DocumentDesignEditResult ApplyEffectSet(DocumentEffectSet effectSet) =>
        ApplyCatalog("Theme Effects", effectSet, static (document, value) => DocumentEffectSet.Apply(document, value));

    public DocumentDesignEditResult SetPageColor(string? colorHex) =>
        UpdatePage(
            page => page.BackgroundColorHex = NormalizePageColor(colorHex),
            "Page Color");

    public DocumentDesignEditResult SetPageBorder(PageBorder? border) =>
        UpdatePage(page => page.PageBorder = border, "Page Border");

    public DocumentDesignEditResult TogglePageBorder(
        string colorHex = "#000000",
        double widthPt = 1.0) =>
        UpdatePage(
            page => page.PageBorder = page.PageBorder is null
                ? new PageBorder(colorHex, widthPt)
                : null,
            "Page Border");

    public DocumentDesignEditResult SetWatermark(WatermarkOptions? options) =>
        UpdatePage(
            page =>
            {
                page.WatermarkOptions = options;
                page.Watermark = null;
            },
            "Watermark");

    public DocumentDesignEditResult SetWatermarkText(string? text) =>
        SetWatermark(string.IsNullOrWhiteSpace(text)
            ? null
            : new WatermarkOptions(text.Trim()));

    public static string? NormalizePageColor(string? colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
            return null;

        var trimmed = colorHex.Trim();
        return trimmed.StartsWith('#') ? trimmed : "#" + trimmed;
    }

    private DocumentDesignEditResult ApplyCatalog<T>(
        string label,
        T value,
        Action<TextDocument, T> apply)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        return Execute(new DesignCatalogCommand(label, document => apply(document, value)));
    }

    private DocumentDesignEditResult Execute(IDocumentCommand command)
    {
        _session.Commands.Execute(command);
        return new DocumentDesignEditResult(Applied: true, command.Label);
    }
}
