using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Paginates documents whose sections do not share one page geometry or header/footer set. WPF's
/// normal <see cref="FlowDocument"/> paginator has one <see cref="DocumentPaginator.PageSize"/> for the
/// whole flow, so it cannot represent a portrait page followed by a landscape page. This paginator
/// exposes one physical page per page box of the section-aware page plan.
/// <para>
/// Each page's body is the real, line-level page the pagination engine laid out for that section
/// (<see cref="PaginationEngine.SegmentPagination"/>) -- not the page box's own block shard. A shard
/// holds whole blocks, so a paragraph running over a page boundary used to print clipped on its first
/// page and leave the page it continued onto blank (header only).
/// </para>
/// </summary>
internal sealed class SectionAwareDocumentPaginator : DocumentPaginator
{
    private readonly IReadOnlyList<PageBox> _pageBoxes;
    private readonly (PaginationEngine.SegmentPagination Segment, int LocalPage)?[] _bodyPages;
    private readonly int[] _sectionPageIndex;
    private readonly Dictionary<(HeaderFooter Slot, double Width), HeaderFooterBandMetrics> _bandMetrics = [];
    private readonly IDocumentPaginatorSource _source;
    private readonly TextDocument _model;
    private Size _pageSize;

    private SectionAwareDocumentPaginator(
        DocumentView sourceEditor,
        PaginatedEditorPanel panel,
        IReadOnlyList<PaginationEngine.SegmentPagination> segments)
    {
        _pageBoxes = panel.PageBoxes.ToArray();
        _model = sourceEditor.Model;
        var (width, height) = PageLayout.PageSizeDip(sourceEditor.Model.Page);
        _pageSize = new Size(width, height);
        _source = new PaginatorSource(this);

        // Body page k of the page plan is page (k - PageOffset) of the segment that owns it. Parity
        // blank pages and the synthetic endnote page own no body page.
        _bodyPages = new (PaginationEngine.SegmentPagination, int)?[_pageBoxes.Count];
        _sectionPageIndex = new int[_pageBoxes.Count];
        var bodyIndex = 0;
        for (var i = 0; i < _pageBoxes.Count; i++)
        {
            var box = _pageBoxes[i];
            _sectionPageIndex[i] = i > 0 && ReferenceEquals(_pageBoxes[i - 1].PageGeometry, box.PageGeometry)
                ? _sectionPageIndex[i - 1] + 1
                : 0;
            if (box.IsParitySyntheticPage || box.IsEndnoteSyntheticPage)
                continue;

            foreach (var segment in segments)
            {
                if (bodyIndex >= segment.PageOffset && bodyIndex < segment.PageOffset + segment.PageCount)
                {
                    _bodyPages[i] = (segment, bodyIndex - segment.PageOffset);
                    break;
                }
            }

            bodyIndex++;
        }

        // Footnote bands of the pages laid out under a footnote reserve, composed in page order so a long
        // note that overflows its band continues at the top of the next page's band.
        var bandPages = new List<(IReadOnlyList<int> FootnoteIds, double BandHeightDip, int PageNumber)>();
        var bandPageIndexes = new List<int>();
        for (var i = 0; i < _pageBoxes.Count; i++)
        {
            if (_bodyPages[i] is not { Segment.FootnoteReserveDip: > 0 } body)
                continue;
            // The engine records which notes it reserved each page for; that, not the block-level page
            // assignment, is what the band shows. Only a chunk's first page owns its notes.
            var ids = body.Segment.PageFootnoteIds is { } reserved
                ? (body.LocalPage == 0 ? reserved : Array.Empty<int>())
                : _pageBoxes[i].FootnoteIds;
            bandPages.Add((ids, body.Segment.FootnoteReserveDip, _pageBoxes[i].PageNumber));
            bandPageIndexes.Add(i);
        }
        if (bandPages.Count > 0)
        {
            var (bandWidth, _) = PageLayout.ContentAreaDip(_model.Page);
            var bands = DocumentNoteRegionPlanner.BuildSequentialFootnoteBands(_model, bandPages, bandWidth);
            for (var k = 0; k < bands.Count && k < bandPageIndexes.Count; k++)
                _footnoteBands[bandPageIndexes[k]] = bands[k];
        }
    }

    private readonly Dictionary<int, DocumentFootnoteContinuationPagePlan> _footnoteBands = [];

    internal static DocumentPaginator Build(DocumentView sourceEditor)
    {
        ArgumentNullException.ThrowIfNull(sourceEditor);
        var segments = new List<PaginationEngine.SegmentPagination>();
        var panel = PaginatedEditorPanel.Build(
            sourceEditor,
            includeParityBlankPages: true,
            segmentPaginations: segments);
        return new SectionAwareDocumentPaginator(sourceEditor, panel, segments);
    }

    public override bool IsPageCountValid => true;

    public override int PageCount => Math.Max(1, _pageBoxes.Count);

    public override Size PageSize
    {
        get => _pageSize;
        set
        {
            if (value.Width > 0 && value.Height > 0
                && !double.IsNaN(value.Width) && !double.IsNaN(value.Height))
                _pageSize = value;
        }
    }

    public override IDocumentPaginatorSource Source => _source;

    public override DocumentPage GetPage(int pageNumber)
    {
        if (pageNumber < 0 || pageNumber >= _pageBoxes.Count)
            return DocumentPage.Missing;

        var box = _pageBoxes[pageNumber];
        var page = box.PageGeometry;
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(page);
        var size = new Size(pageWidth, pageHeight);
        var (marginLeft, marginTop, _, marginBottom) = PageLayout.MarginsDip(page);
        var (contentWidth, contentHeight) = PageLayout.ContentAreaDip(page);
        var body = _bodyPages[pageNumber];
        var bodyTop = marginTop + (body?.Segment.TopInsetDip ?? 0);
        var bodyBottom = pageHeight - marginBottom - (body?.Segment.BottomInsetDip ?? 0);

        var visual = new ContainerVisual();
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageWidth, pageHeight));
        visual.Children.Add(background);

        if (!string.IsNullOrEmpty(page.Watermark))
            visual.Children.Add(HeaderFooterPaginator.BuildWatermark(page.Watermark!, size));

        // "First page" / "all but first page" borders count pages within the section that owns them.
        var border = page.PageBorder;
        var hasBorder = border is not null
            && PageBorderVisibilityPlanner.ShouldRender(border.Display, _sectionPageIndex[pageNumber]);
        if (hasBorder && PageBorderVisibilityPlanner.LayerFor(border!.ZOrder) == PageBorderRenderLayer.BehindText)
            visual.Children.Add(HeaderFooterPaginator.BuildPageBorder(border, size, page, _model));

        if (body is { } bodyPage)
        {
            AddSegmentBodyVisual(visual, bodyPage.Segment, bodyPage.LocalPage);
        }
        else
        {
            AddShardBodyVisual(visual, box, page, marginLeft, marginTop, contentWidth, contentHeight);
        }

        if (page.ColumnsLineBetween && page.ColumnCount > 1)
            visual.Children.Add(DocumentView.BuildColumnRuleVisual(
                page,
                marginLeft,
                bodyTop,
                contentWidth,
                bodyBottom));

        AddNoteRegionVisual(
            visual,
            pageNumber,
            box,
            marginLeft,
            bodyTop,
            contentWidth,
            Math.Max(0, bodyBottom - bodyTop),
            pageHeight,
            notesInsideBody: body is { Segment.FootnoteReserveDip: > 0 });

        AddHeaderFooterBand(visual, box, box.HeaderSlotContent, isHeader: true, page, pageHeight, marginLeft, contentWidth);
        AddHeaderFooterBand(visual, box, box.FooterSlotContent, isHeader: false, page, pageHeight, marginLeft, contentWidth);

        if (hasBorder && PageBorderVisibilityPlanner.LayerFor(border!.ZOrder) == PageBorderRenderLayer.InFrontOfText)
            visual.Children.Add(HeaderFooterPaginator.BuildPageBorder(border, size, page, _model));

        return new DocumentPage(
            visual,
            size,
            new Rect(0, 0, pageWidth, pageHeight),
            new Rect(marginLeft, bodyTop, contentWidth, Math.Max(0, bodyBottom - bodyTop)));
    }

    /// <summary>
    /// Adds the real laid-out page <paramref name="localPage"/> of <paramref name="segment"/>. The
    /// segment's flow is paginated at the full sheet size with the margins as padding, so its page
    /// visual is already in sheet coordinates.
    /// </summary>
    private static void AddSegmentBodyVisual(
        ContainerVisual container,
        PaginationEngine.SegmentPagination segment,
        int localPage)
    {
        var page = segment.Paginator.GetPage(localPage);
        if (page == DocumentPage.Missing || page.Visual is null)
            return;

        // A repeat GetPage (PDF export asks twice per page) hands back the same cached visual.
        if (VisualTreeHelper.GetParent(page.Visual) is ContainerVisual previous)
            previous.Children.Remove(page.Visual);
        container.Children.Add(page.Visual);

        // Outline / shadow / glow / reflection, painted over the body exactly as the single-flow print
        // path does (TextEffectsPrintPaginator); the segment page is already in sheet coordinates.
        if (segment.TextEffectPlans is { Count: > 0 } plans
            && TextEffectsPrintPaginator.BuildEffectsVisual(page.Visual, plans) is { } effects)
        {
            container.Children.Add(effects);
        }
    }

    /// <summary>Fallback when no segment pagination is available: the page box's own block shard.</summary>
    private static void AddShardBodyVisual(
        ContainerVisual container,
        PageBox box,
        PageSettings page,
        double marginLeft,
        double marginTop,
        double contentWidth,
        double contentHeight)
    {
        var bodyPage = PaginateFlow(box.Body.Document, page, contentWidth, contentHeight);
        if (bodyPage is null)
            return;

        var bodyVisual = new DrawingVisual();
        using (var dc = bodyVisual.RenderOpen())
        {
            dc.DrawRectangle(
                new VisualBrush(bodyPage.Visual) { Stretch = Stretch.None },
                null,
                new Rect(marginLeft, marginTop, contentWidth, contentHeight));
        }
        container.Children.Add(bodyVisual);
    }

    private static DocumentPage? PaginateFlow(
        FlowDocument flow,
        PageSettings page,
        double width,
        double height)
    {
        flow.PageWidth = Math.Max(1, width);
        flow.PageHeight = Math.Max(1, height);
        flow.PagePadding = new Thickness(0);
        DocumentView.ApplyColumnLayout(flow, page, useNativeColumnRule: false);
        if (double.IsInfinity(flow.ColumnWidth))
            flow.ColumnWidth = Math.Max(1, width);

        var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        paginator.PageSize = new Size(Math.Max(1, width), Math.Max(1, height));
        paginator.ComputePageCount();
        return paginator.PageCount > 0 ? paginator.GetPage(0) : null;
    }

    /// <summary>
    /// Paints this page box's footnote/endnote text. With <paramref name="notesInsideBody"/> the notes
    /// end at the bottom of the body area, inside the space the pagination reserved for them (where
    /// Word puts them); otherwise they follow the body area into the bottom margin, mirroring the note
    /// band the single-geometry print path draws (see <c>HeaderFooterPaginator.BuildNotesAtFoot</c>).
    /// </summary>
    private void AddNoteRegionVisual(
        ContainerVisual container,
        int pageIndex,
        PageBox box,
        double marginLeft,
        double marginTop,
        double contentWidth,
        double contentHeight,
        double pageHeight,
        bool notesInsideBody)
    {
        if (contentWidth <= 0)
            return;

        if (_footnoteBands.TryGetValue(pageIndex, out var band))
        {
            if (band.HasContent)
                AddFootnoteBandVisual(container, band, box, marginLeft, marginTop + contentHeight, contentWidth);
            return;
        }

        // Pages the per-page footnote engine laid out without a band carry no footnotes of their own:
        // their references were reserved on the page that shows them.
        if (_bodyPages[pageIndex] is { Segment.PageFootnoteIds: not null } && box.FootnoteIds.Count > 0)
            return;

        var footnoteIds = box.FootnoteIds;
        var endnoteIds = box.EndnoteIds;
        if (footnoteIds.Count == 0 && endnoteIds.Count == 0)
            return;

        var plan = footnoteIds.Count > 0
            ? DocumentNoteRegionPlanner.BuildFootnoteRegion(_model, footnoteIds, box.PageNumber, contentWidth)
            : DocumentNoteRegionPlanner.BuildEndnoteRegion(
                _model, endnoteIds, box.PageNumber, contentWidth, box.IsEndnoteSyntheticPage);
        if (plan.Rows.Count == 0)
            return;

        var visual = new ContainerVisual();
        var rules = new DrawingVisual();
        visual.Children.Add(rules);
        var noteFont = new FontFamily(plan.TextFontFamily ?? "Calibri");
        // Inside the reserved band the notes follow Word's own stack: a separator line box (the rule sits
        // in its middle), then each note line by line with the note paragraph's own space after.
        var wordStack = notesInsideBody && footnoteIds.Count > 0;
        using (var dc = rules.RenderOpen())
        {
            var contentBottom = marginTop + contentHeight;
            var y = wordStack
                ? Math.Max(marginTop, contentBottom - plan.EstimatedHeightDip)
                : contentBottom + PageLayout.PointsToDip(3);
            var maxY = pageHeight - PageLayout.PointsToDip(4);

            if (plan.Kind == DocumentNoteRegionKind.Endnotes && box.IsEndnoteSyntheticPage
                && plan.Heading is { Length: > 0 } heading && y < maxY)
            {
                var headingText = PrintPlainText.Build(
                    heading,
                    noteFont,
                    PageLayout.PointsToDip(plan.TextFontSizePt + 2),
                    Brushes.Black,
                    contentWidth,
                    weight: FontWeights.Bold);
                y += PrintPlainText.Place(visual, headingText, marginLeft, y) + PageLayout.PointsToDip(2);
            }

            if (y < maxY)
            {
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)), 0.5);
                var ruleY = wordStack ? y + plan.SeparatorHeightDip / 2 : y;
                dc.DrawLine(
                    pen,
                    new Point(marginLeft + plan.SeparatorXOffsetDip, ruleY),
                    new Point(marginLeft + plan.SeparatorXOffsetDip + plan.SeparatorWidthDip, ruleY));
                y += wordStack ? plan.SeparatorHeightDip : PageLayout.PointsToDip(2);
            }

            foreach (var row in plan.Rows)
            {
                if (y >= maxY)
                    break;

                var label = string.IsNullOrEmpty(row.Label) ? string.Empty : $"{row.Label}. ";
                var text = PrintPlainText.Build(
                    label + row.Text,
                    noteFont,
                    PageLayout.PointsToDip(plan.TextFontSizePt),
                    Brushes.Black,
                    contentWidth);
                var placed = PrintPlainText.Place(visual, text, marginLeft, y);
                y += wordStack
                    ? Math.Max(row.EstimatedHeightDip, placed) + plan.RowGapDip
                    : Math.Max(row.EstimatedHeightDip, placed) + PageLayout.PointsToDip(1);
            }
        }

        container.Children.Add(visual);
    }

    /// <summary>
    /// Paints one page's footnote band, bottom-aligned in the space its pagination reserved: Word's
    /// separator (a continuation separator spanning the column when the band opens with the rest of an
    /// earlier page's note), then each note fragment line by line.
    /// </summary>
    private void AddFootnoteBandVisual(
        ContainerVisual container,
        DocumentFootnoteContinuationPagePlan band,
        PageBox box,
        double marginLeft,
        double bodyBottom,
        double contentWidth)
    {
        var ids = box.FootnoteIds.Count > 0 ? box.FootnoteIds : band.Fragments.Select(f => f.NoteId).Distinct().ToList();
        var metricsPlan = DocumentNoteRegionPlanner.BuildFootnoteRegion(_model, ids, box.PageNumber, contentWidth);
        var noteFont = new FontFamily(metricsPlan.TextFontFamily ?? "Calibri");
        var visual = new ContainerVisual();
        var rules = new DrawingVisual();
        visual.Children.Add(rules);
        var y = bodyBottom - band.AvailableHeightDip;
        using (var dc = rules.RenderOpen())
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)), 0.5);
            var ruleWidth = band.SeparatorKind == DocumentFootnoteSeparatorKind.Continuation
                ? contentWidth
                : metricsPlan.SeparatorWidthDip;
            var ruleY = y + metricsPlan.SeparatorHeightDip / 2;
            dc.DrawLine(pen, new Point(marginLeft, ruleY), new Point(marginLeft + ruleWidth, ruleY));
        }

        y += metricsPlan.SeparatorHeightDip;
        foreach (var fragment in band.Fragments)
        {
            var label = string.IsNullOrEmpty(fragment.Label) ? string.Empty : $"{fragment.Label}. ";
            var text = PrintPlainText.Build(
                label + fragment.Text,
                noteFont,
                PageLayout.PointsToDip(metricsPlan.TextFontSizePt),
                Brushes.Black,
                contentWidth);
            PrintPlainText.Place(visual, text, marginLeft, y);
            y += fragment.EstimatedHeightDip + (fragment.EndsNote ? metricsPlan.RowGapDip : 0);
        }

        container.Children.Add(visual);
    }

    /// <summary>
    /// Lays out the page's header or footer slot with <see cref="HeaderFooterBandRenderer"/> at the
    /// section's own "from edge" distance and natural height. (The page box's in-page sub-editor is a
    /// fixed 36-DIP strip; rasterising it stretched the text to the content width and clipped anything
    /// past the first lines.)
    /// </summary>
    private void AddHeaderFooterBand(
        ContainerVisual container,
        PageBox box,
        HeaderFooter? slot,
        bool isHeader,
        PageSettings page,
        double pageHeight,
        double marginLeft,
        double contentWidth)
    {
        if (slot is not { IsEmpty: false } || contentWidth <= 0)
            return;

        var maxHeight = HeaderFooterBandRenderer.MaxBandHeightDip(page);
        if (!_bandMetrics.TryGetValue((slot, contentWidth), out var metrics))
        {
            _bandMetrics[(slot, contentWidth)] = metrics =
                HeaderFooterBandRenderer.Measure(_model, slot, isHeader, contentWidth, maxHeight);
        }

        var context = new HeaderFooterBandContext(
            box.PageNumber,
            box.PageNumberText,
            Math.Max(1, box.FieldPageCount),
            Math.Max(1, box.SectionOrdinal),
            Math.Max(1, box.SectionPageCount));
        var top = isHeader
            ? HeaderFooterBandRenderer.HeaderDistanceDip(page)
            : Math.Max(0, pageHeight - HeaderFooterBandRenderer.FooterDistanceDip(page) - metrics.HeightDip);
        if (HeaderFooterBandRenderer.Render(
                _model, slot, isHeader, context, contentWidth, metrics, maxHeight, page, new Point(marginLeft, top)) is not { } band)
            return;

        band.Offset = new Vector(marginLeft, top);
        container.Children.Add(band);
    }

    private sealed class PaginatorSource(DocumentPaginator paginator) : IDocumentPaginatorSource
    {
        public DocumentPaginator DocumentPaginator { get; } = paginator;
    }
}
