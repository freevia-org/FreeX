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
/// Callers pass a cloned <see cref="PageSettings"/> instance with the desired mutations applied, so
/// the command copies the full page setup surface and remains suitable for undoable column/layout
/// changes as well as the Page Setup dialog's geometry changes.
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
    /// Copies the full page setup surface from <paramref name="src"/> into <paramref name="dst"/>
    /// in-place. Mutable collections are cloned to keep undo snapshots independent.
    /// </summary>
    private static void CopyTo(PageSettings src, PageSettings dst)
    {
        dst.WidthPt = src.WidthPt;
        dst.HeightPt = src.HeightPt;
        dst.Landscape = src.Landscape;
        dst.MarginLeftPt = src.MarginLeftPt;
        dst.MarginRightPt = src.MarginRightPt;
        dst.MarginTopPt = src.MarginTopPt;
        dst.MarginBottomPt = src.MarginBottomPt;
        dst.GutterPt = src.GutterPt;
        dst.HeaderDistancePt = src.HeaderDistancePt;
        dst.FooterDistancePt = src.FooterDistancePt;
        dst.MirrorMargins = src.MirrorMargins;
        dst.GutterAtTop = src.GutterAtTop;
        dst.ColumnCount = src.ColumnCount;
        dst.ColumnSpacingPt = src.ColumnSpacingPt;
        dst.ColumnsLineBetween = src.ColumnsLineBetween;
        dst.ColumnWidthsPt = src.ColumnWidthsPt is null ? null : new List<double>(src.ColumnWidthsPt);
        dst.PageBorder = src.PageBorder;
        dst.Watermark = src.Watermark;
        dst.WatermarkOptions = PageSettings.CloneWatermarkOptions(src.WatermarkOptions);
        dst.LineNumberMode = src.LineNumberMode;
        dst.LineNumberCountBy = src.LineNumberCountBy;
        dst.LineNumberStartAt = src.LineNumberStartAt;
        dst.PageNumberFormat = src.PageNumberFormat;
        dst.PageNumberStartAt = src.PageNumberStartAt;
        dst.PageNumberChapterStyleLevel = src.PageNumberChapterStyleLevel;
        dst.PageNumberChapterSeparator = src.PageNumberChapterSeparator;
        dst.AutoHyphenation = src.AutoHyphenation;
        dst.HyphenationZonePt = src.HyphenationZonePt;
        dst.ConsecutiveHyphenLimit = src.ConsecutiveHyphenLimit;
        dst.DoNotHyphenateCaps = src.DoNotHyphenateCaps;
        dst.DefaultTabStopPt = src.DefaultTabStopPt;
        dst.VerticalAlignment = src.VerticalAlignment;
        dst.DifferentFirstPage = src.DifferentFirstPage;
        dst.DifferentOddEvenPages = src.DifferentOddEvenPages;
        dst.BackgroundColorHex = src.BackgroundColorHex;
    }
}
