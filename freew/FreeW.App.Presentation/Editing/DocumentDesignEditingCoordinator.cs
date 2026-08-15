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
    private DocumentDesignPreviewSnapshot? _previewSnapshot;

    internal DocumentDesignEditingCoordinator(DocumentEditingSession session) => _session = session;

    public bool HasActivePreview => _previewSnapshot is not null;

    public DocumentDesignEditResult ApplyDocumentProperties(DocumentPropertiesDialogValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Execute(new ApplyDocumentPropertiesCommand(values));
    }

    public DocumentDesignEditResult UpdatePage(
        Action<PageSettings> mutation,
        int sectionIndex = -1,
        string label = "Page Setup")
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        var settings = PageSettingsSectionResolver.Resolve(_session.Document, sectionIndex).Clone();
        mutation(settings);
        return SetPageSettings(settings, sectionIndex, label);
    }

    public DocumentDesignEditResult SetPageSettings(
        PageSettings settings,
        int sectionIndex = -1,
        string label = "Page Setup")
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return Execute(new SetPageSettingsCommand(settings.Clone(), sectionIndex, label));
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

    public void PreviewTheme(DocumentTheme theme) =>
        PreviewCatalog(theme, static (document, value) => DocumentTheme.Apply(document, value));

    public void PreviewThemeColors(DocumentTheme theme) =>
        PreviewCatalog(theme, static (document, value) => DocumentTheme.ApplyColors(document, value));

    public void PreviewStyleSet(DocumentStyleSet styleSet) =>
        PreviewCatalog(styleSet, static (document, value) => DocumentStyleSet.Apply(document, value));

    public void PreviewFontSet(DocumentFontSet fontSet) =>
        PreviewCatalog(fontSet, static (document, value) => DocumentFontSet.Apply(document, value));

    public void PreviewParagraphSpacingSet(DocumentParagraphSpacingSet spacingSet) =>
        PreviewCatalog(spacingSet, static (document, value) => DocumentParagraphSpacingSet.Apply(document, value));

    public void PreviewEffectSet(DocumentEffectSet effectSet) =>
        PreviewCatalog(effectSet, static (document, value) => DocumentEffectSet.Apply(document, value));

    /// <summary>Restores the exact pre-hover design catalog without adding an undo entry.</summary>
    public bool CancelPreview()
    {
        if (_previewSnapshot is not { } snapshot)
            return false;

        RestorePreviewSnapshot(_session.Document, snapshot);
        _previewSnapshot = null;
        return true;
    }

    public DocumentDesignEditResult SetPageColor(string? colorHex, int sectionIndex = -1) =>
        UpdatePage(
            page => page.BackgroundColorHex = NormalizePageColor(colorHex),
            sectionIndex,
            "Page Color");

    public DocumentDesignEditResult SetPageBorder(PageBorder? border, int sectionIndex = -1) =>
        UpdatePage(page => page.PageBorder = border, sectionIndex, "Page Border");

    public DocumentDesignEditResult TogglePageBorder(
        string colorHex = "#000000",
        double widthPt = 1.0,
        int sectionIndex = -1) =>
        UpdatePage(
            page => page.PageBorder = page.PageBorder is null
                ? new PageBorder(colorHex, widthPt)
                : null,
            sectionIndex,
            "Page Border");

    public DocumentDesignEditResult SetWatermark(WatermarkOptions? options, int sectionIndex = -1) =>
        UpdatePage(
            page =>
            {
                page.WatermarkOptions = options;
                page.Watermark = null;
            },
            sectionIndex,
            "Watermark");

    public DocumentDesignEditResult SetWatermarkText(string? text, int sectionIndex = -1) =>
        SetWatermark(string.IsNullOrWhiteSpace(text)
            ? null
            : new WatermarkOptions(text.Trim()), sectionIndex);

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

    private void PreviewCatalog<T>(T value, Action<TextDocument, T> apply)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);

        var document = _session.Document;
        if (_previewSnapshot is null)
            _previewSnapshot = CapturePreviewSnapshot(document);
        else
            RestorePreviewSnapshot(document, _previewSnapshot);

        apply(document, value);
    }

    private DocumentDesignEditResult Execute(IDocumentCommand command)
    {
        CancelPreview();
        _session.Commands.Execute(command);
        return new DocumentDesignEditResult(Applied: true, command.Label);
    }

    private static DocumentDesignPreviewSnapshot CapturePreviewSnapshot(TextDocument document) =>
        new(
            document.Theme,
            document.DefaultRun,
            document.DefaultParagraph,
            document.Styles.ToDictionary(
                pair => pair.Key,
                pair => new DocumentDesignStylePreviewSnapshot(
                    pair.Value,
                    pair.Value.Run,
                    pair.Value.Paragraph)));

    private static void RestorePreviewSnapshot(
        TextDocument document,
        DocumentDesignPreviewSnapshot snapshot)
    {
        document.Theme = snapshot.Theme;
        document.DefaultRun = snapshot.DefaultRun;
        document.DefaultParagraph = snapshot.DefaultParagraph;

        foreach (var styleId in document.Styles.Keys.Except(snapshot.Styles.Keys).ToArray())
            document.Styles.Remove(styleId);

        foreach (var (styleId, styleSnapshot) in snapshot.Styles)
        {
            styleSnapshot.Style.Run = styleSnapshot.Run;
            styleSnapshot.Style.Paragraph = styleSnapshot.Paragraph;
            document.Styles[styleId] = styleSnapshot.Style;
        }
    }

    private sealed record DocumentDesignPreviewSnapshot(
        DocumentTheme Theme,
        RunFormatting DefaultRun,
        ParagraphFormatting DefaultParagraph,
        IReadOnlyDictionary<string, DocumentDesignStylePreviewSnapshot> Styles);

    private sealed record DocumentDesignStylePreviewSnapshot(
        DocumentStyle Style,
        RunFormatting Run,
        ParagraphFormatting Paragraph);
}
