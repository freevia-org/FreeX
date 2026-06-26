namespace FreeW.Core.Model;

/// <summary>
/// Undoable command that replaces the document-level <see cref="PageSettings"/> values on
/// <see cref="TextDocument.Page"/> with the values supplied in <paramref name="settings"/>.
///
/// <para>
/// Because <see cref="TextDocument.Page"/> is a shared mutable instance (not a replaceable
/// property), Apply/Revert copy individual property values in and out rather than swapping the
/// object reference.  A <see cref="Clone"/> snapshot of the pre-apply state is captured on the
/// first <see cref="Apply"/> call and restored by <see cref="Revert"/>.
/// </para>
///
/// <para>
/// Only the properties exposed by the Page Setup dialog (size, margins, orientation) are
/// copied; advanced properties such as columns, borders, watermarks, and line numbering are
/// preserved unchanged.
/// </para>
/// </summary>
public sealed class SetPageSettingsCommand(PageSettings settings) : IDocumentCommand
{
    private PageSettings? _previous;

    public string Label => "Page Setup";

    public void Apply(IDocumentCommandContext context)
    {
        var page = context.Document.Page;
        // Snapshot for undo on first Apply.
        _previous ??= page.Clone();
        CopyTo(settings, page);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null)
            return;
        CopyTo(_previous, context.Document.Page);
        _previous = null;
    }

    /// <summary>
    /// Copies the page-setup-dialog subset of properties from <paramref name="src"/> into
    /// <paramref name="dst"/> in-place, leaving all other properties on <paramref name="dst"/>
    /// untouched.
    /// </summary>
    private static void CopyTo(PageSettings src, PageSettings dst)
    {
        dst.WidthPt       = src.WidthPt;
        dst.HeightPt      = src.HeightPt;
        dst.Landscape     = src.Landscape;
        dst.MarginLeftPt  = src.MarginLeftPt;
        dst.MarginRightPt = src.MarginRightPt;
        dst.MarginTopPt   = src.MarginTopPt;
        dst.MarginBottomPt = src.MarginBottomPt;
    }
}
