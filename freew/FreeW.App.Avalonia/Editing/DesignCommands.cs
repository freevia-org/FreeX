using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

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
