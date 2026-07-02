using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

/// <summary>
/// AV-DESIGN: undoable Design-tab document mutations for the Avalonia shell. Each command mutates a
/// document-wide aspect — the theme colour/font scheme, a font set, a paragraph-spacing set, the page
/// background colour, the page border, or the page watermark — and is reversible through the
/// <see cref="DocumentCommandBus"/> so a single Undo restores the prior state exactly.
///
/// <para>
/// The catalog-apply commands (theme / colours / fonts / spacing) call the pure model helpers
/// (<see cref="DocumentTheme.Apply"/> etc.) which rewrite several built-in styles plus the document
/// defaults and <see cref="TextDocument.Theme"/>. Rather than hand-track every touched field, each
/// command snapshots the full set of affected style formatting + defaults + theme on first
/// <see cref="IDocumentCommand.Apply"/> and restores that snapshot on <see cref="IDocumentCommand.Revert"/>.
/// The page commands snapshot the single <see cref="PageSettings"/> field they change.
/// </para>
/// </summary>
internal static class DesignCommandHelpers
{
    /// <summary>
    /// The built-in styles any Design catalog-apply may rewrite. A superset is safe — snapshotting a style
    /// the apply did not touch simply restores an unchanged value.
    /// </summary>
    internal static readonly string[] AffectedStyleIds =
        ["Normal", "Title", "Subtitle", "Heading1", "Heading2", "Heading3", "Quote"];
}

/// <summary>
/// A reversible document-wide catalog mutation (theme / colours / fonts / paragraph-spacing). The
/// <paramref name="apply"/> delegate performs the real change via the pure model helpers; the command
/// captures and restores the document default run/paragraph, the theme, and the affected built-in styles'
/// run + paragraph formatting around it so Undo is exact.
/// </summary>
internal sealed class DesignCatalogCommand(string label, Action<TextDocument> apply) : IDocumentCommand
{
    public string Label => label;

    // Lazily captured on the first Apply so Revert restores the pre-apply state.
    private RunFormatting? _defaultRun;
    private ParagraphFormatting? _defaultParagraph;
    private DocumentTheme? _theme;
    private (RunFormatting Run, ParagraphFormatting Paragraph)?[]? _styleSnapshots;

    public void Apply(IDocumentCommandContext context)
    {
        var doc = context.Document;
        if (_defaultRun is null)
        {
            _defaultRun = doc.DefaultRun;
            _defaultParagraph = doc.DefaultParagraph;
            _theme = doc.Theme;
            _styleSnapshots = new (RunFormatting, ParagraphFormatting)?[DesignCommandHelpers.AffectedStyleIds.Length];
            for (var i = 0; i < DesignCommandHelpers.AffectedStyleIds.Length; i++)
            {
                if (doc.Styles.TryGetValue(DesignCommandHelpers.AffectedStyleIds[i], out var style))
                    _styleSnapshots[i] = (style.Run, style.Paragraph);
            }
        }

        apply(doc);
    }

    public void Revert(IDocumentCommandContext context)
    {
        var doc = context.Document;
        if (_defaultRun is null || _styleSnapshots is null)
            return;

        doc.DefaultRun = _defaultRun;
        doc.DefaultParagraph = _defaultParagraph!;
        doc.Theme = _theme!;
        for (var i = 0; i < DesignCommandHelpers.AffectedStyleIds.Length; i++)
        {
            if (_styleSnapshots[i] is { } snap
                && doc.Styles.TryGetValue(DesignCommandHelpers.AffectedStyleIds[i], out var style))
            {
                style.Run = snap.Run;
                style.Paragraph = snap.Paragraph;
            }
        }
    }
}

/// <summary>Reversibly set (or clear) the whole-page background colour (<c>w:background</c>).</summary>
internal sealed class SetPageColorCommand(string? colorHex) : IDocumentCommand
{
    public string Label => "Page Color";

    private string? _previous;
    private bool _captured;

    public void Apply(IDocumentCommandContext context)
    {
        var page = context.Document.Page;
        if (!_captured)
        {
            _previous = page.BackgroundColorHex;
            _captured = true;
        }
        page.BackgroundColorHex = colorHex;
    }

    public void Revert(IDocumentCommandContext context) =>
        context.Document.Page.BackgroundColorHex = _previous;
}

/// <summary>Reversibly set (or clear) the page border (<c>w:pgBorders</c>).</summary>
internal sealed class SetPageBorderCommand(PageBorder? border) : IDocumentCommand
{
    public string Label => "Page Border";

    private PageBorder? _previous;
    private bool _captured;

    public void Apply(IDocumentCommandContext context)
    {
        var page = context.Document.Page;
        if (!_captured)
        {
            _previous = page.PageBorder;
            _captured = true;
        }
        page.PageBorder = border;
    }

    public void Revert(IDocumentCommandContext context) =>
        context.Document.Page.PageBorder = _previous;
}

/// <summary>
/// Reversibly set (or clear) the page watermark options. Captures and restores both
/// <see cref="PageSettings.WatermarkOptions"/> and the legacy <see cref="PageSettings.Watermark"/> string
/// so Undo restores the exact prior state. Applying clears the legacy string so the new options drive the
/// render entirely.
/// </summary>
internal sealed class SetWatermarkCommand(WatermarkOptions? options) : IDocumentCommand
{
    public string Label => "Watermark";

    private WatermarkOptions? _prevOptions;
    private string? _prevLegacy;
    private bool _captured;

    public void Apply(IDocumentCommandContext context)
    {
        var page = context.Document.Page;
        if (!_captured)
        {
            _prevOptions = page.WatermarkOptions;
            _prevLegacy = page.Watermark;
            _captured = true;
        }
        page.WatermarkOptions = options;
        page.Watermark = null;
    }

    public void Revert(IDocumentCommandContext context)
    {
        var page = context.Document.Page;
        page.WatermarkOptions = _prevOptions;
        page.Watermark = _prevLegacy;
    }
}
