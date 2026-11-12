using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using DocumentFloatingObjectSnapshot = FreeW.App.Presentation.DocumentView.DocumentFloatingObjectSnapshot;
using ModelParagraph = FreeW.Core.Model.Paragraph;
using ModelTextAlignment = FreeW.Core.Model.TextAlignment;

namespace FreeW.App.Host;

/// <summary>Field values a header/footer band resolves PAGE/NUMPAGES/SECTION fields against.</summary>
internal readonly record struct HeaderFooterBandContext(
    int PageNumber,
    string? PageNumberText,
    int PageCount,
    int SectionOrdinal = 1,
    int SectionPageCount = 0);

/// <summary>
/// Layout of one header/footer slot at a given width: the height its in-flow paragraphs occupy and
/// the vertical offset of every framed paragraph (see <see cref="ModelParagraph.FrameHorizontalAlignment"/>).
/// Independent of the page number, so one measurement serves every page that shows the slot.
/// </summary>
internal sealed record HeaderFooterBandMetrics(double HeightDip, IReadOnlyList<double> FrameTopsDip);

/// <summary>
/// Renders a header or footer slot for Print / Print Preview / PDF / XPS the way the body is rendered:
/// through the same model-to-FlowDocument mapping (<see cref="DocumentView"/>), so every paragraph, its
/// alignment, indents and spacing, tab stops (including the Header/Footer styles' centre and right
/// stops), run formatting, fields, inline images and Word's no-kerning default match the body text —
/// instead of the single flattened 11pt Calibri line the print overlay used to draw.
/// </summary>
internal static class HeaderFooterBandRenderer
{
    private const double MeasureToleranceDip = 0.5;
    private const double SentinelLineHeightDip = 1.0;

    /// <summary>
    /// Text ending exactly on a right tab stop at the right margin (the Footer style's right stop) must
    /// not wrap for a sub-pixel measuring difference, so the line box gets this much slack.
    /// </summary>
    private const double TabStopSlackDip = 1.5;

    /// <summary>Word's own "Header/Footer from edge" default (0.5") for a section that leaves it unspecified.</summary>
    private const double DefaultEdgeDistancePt = 36;

    internal static double HeaderDistanceDip(PageSettings page) =>
        PageLayout.PointsToDip(page.HeaderDistancePt > 0 ? page.HeaderDistancePt : DefaultEdgeDistancePt);

    internal static double FooterDistanceDip(PageSettings page) =>
        PageLayout.PointsToDip(page.FooterDistancePt > 0 ? page.FooterDistancePt : DefaultEdgeDistancePt);

    /// <summary>A header/footer can never claim more than this share of the sheet.</summary>
    internal static double MaxBandHeightDip(PageSettings page) =>
        Math.Max(1, PageLayout.PageSizeDip(page).Height / 2);

    /// <summary>
    /// How far a header taller than the top margin (or a footer taller than the bottom margin) pushes
    /// the body text area in, matching Word: the body starts below the header's last line rather than
    /// at the margin, and ends above the footer's first line.
    /// </summary>
    internal static (double TopDip, double BottomDip) ResolveBodyInsetsDip(
        TextDocument model,
        PageSettings page,
        HeaderFooter? header,
        HeaderFooter? footer)
    {
        var (contentWidthDip, _) = PageLayout.ContentAreaDip(page);
        if (contentWidthDip <= 0)
            return (0, 0);

        var (_, marginTop, _, marginBottom) = PageLayout.MarginsDip(page);
        var maxHeight = MaxBandHeightDip(page);
        var top = 0.0;
        var bottom = 0.0;
        if (header is { IsEmpty: false } && page.MarginTopPt >= 0)
        {
            var height = Measure(model, header, isHeader: true, contentWidthDip, maxHeight).HeightDip;
            top = Math.Max(0, HeaderDistanceDip(page) + height - marginTop);
        }

        if (footer is { IsEmpty: false } && page.MarginBottomPt >= 0)
        {
            var height = Measure(model, footer, isHeader: false, contentWidthDip, maxHeight).HeightDip;
            bottom = Math.Max(0, FooterDistanceDip(page) + height - marginBottom);
        }

        return (top, bottom);
    }

    /// <summary>Measures <paramref name="slot"/> at <paramref name="widthDip"/>, capped at <paramref name="maxHeightDip"/>.</summary>
    internal static HeaderFooterBandMetrics Measure(
        TextDocument model,
        HeaderFooter slot,
        bool isHeader,
        double widthDip,
        double maxHeightDip)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(slot);
        if (widthDip <= 0 || maxHeightDip <= 0 || slot.IsEmpty)
            return new HeaderFooterBandMetrics(0, []);

        var context = new HeaderFooterBandContext(1, null, 1);
        var (inFlow, framed) = Partition(slot);
        var flowHeight = inFlow.Count == 0 && slot.Table is null
            ? 0
            : MeasureFlowHeight(BuildFlow(model, inFlow, slot.Table, isHeader, context, widthDip, null), widthDip, maxHeightDip);

        var frameTops = new List<double>(framed.Count);
        var height = flowHeight;
        foreach (var (paragraph, precedingInFlowCount) in framed)
        {
            // A text-anchored frame starts level with the paragraph that follows it in the story.
            var top = precedingInFlowCount == 0
                ? 0
                : MeasureFlowHeight(
                    BuildFlow(model, inFlow.Take(precedingInFlowCount).ToList(), null, isHeader, context, widthDip, null),
                    widthDip,
                    maxHeightDip);
            frameTops.Add(top);
            var frameHeight = MeasureFlowHeight(
                BuildFlow(model, [paragraph], null, isHeader, context, widthDip, paragraph.FrameHorizontalAlignment),
                widthDip,
                maxHeightDip);
            height = Math.Max(height, Math.Min(maxHeightDip, top + frameHeight));
        }

        return new HeaderFooterBandMetrics(height, frameTops);
    }

    /// <summary>
    /// Builds the visual for <paramref name="slot"/> with its top-left corner at the origin; the caller
    /// positions it. Returns null when there is nothing to draw.
    /// </summary>
    internal static ContainerVisual? Render(
        TextDocument model,
        HeaderFooter slot,
        bool isHeader,
        HeaderFooterBandContext context,
        double widthDip,
        HeaderFooterBandMetrics metrics,
        double maxHeightDip,
        PageSettings? page = null,
        Point bandOriginDip = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(metrics);
        if (widthDip <= 0 || maxHeightDip <= 0 || slot.IsEmpty)
            return null;

        var band = new BandVisual();
        var (inFlow, framed) = Partition(slot);
        if (inFlow.Count > 0 || slot.Table is not null)
        {
            AddFlowPage(
                band,
                band,
                BuildFlow(model, inFlow, slot.Table, isHeader, context, widthDip, null),
                widthDip,
                maxHeightDip,
                0);
        }

        for (var index = 0; index < framed.Count; index++)
        {
            var paragraph = framed[index].Paragraph;
            var top = index < metrics.FrameTopsDip.Count ? metrics.FrameTopsDip[index] : 0;
            AddFlowPage(
                band,
                band,
                BuildFlow(model, [paragraph], null, isHeader, context, widthDip, paragraph.FrameHorizontalAlignment),
                widthDip,
                Math.Max(1, maxHeightDip - top),
                top);
        }

        if (page is not null)
            AddFloatingObjects(band, model, slot, inFlow, isHeader, context, page, bandOriginDip, widthDip, maxHeightDip);

        return band.Children.Count == 0 ? null : band;
    }

    /// <summary>
    /// Draws the slot's anchored drawings -- DrawingML floating pictures/shapes in its paragraphs and
    /// positioned legacy VML text boxes (<see cref="ModelParagraph.LegacyTextBoxFrame"/>, e.g. Word's
    /// circled page number in the bottom margin) -- at their page/margin/paragraph-relative positions.
    /// Positions are computed in page space and the layers are offset by -<paramref name="bandOriginDip"/>,
    /// since the caller places the band at that origin. Behind-text objects go under the band's text,
    /// the rest above it. Fields inside a text box resolve against <paramref name="context"/> (this page).
    /// </summary>
    private static void AddFloatingObjects(
        BandVisual band,
        TextDocument model,
        HeaderFooter slot,
        IReadOnlyList<ModelParagraph> inFlow,
        bool isHeader,
        HeaderFooterBandContext context,
        PageSettings page,
        Point bandOriginDip,
        double widthDip,
        double maxHeightDip)
    {
        var behind = new ContainerVisual { Offset = new Vector(-bandOriginDip.X, -bandOriginDip.Y) };
        var front = new ContainerVisual { Offset = new Vector(-bandOriginDip.X, -bandOriginDip.Y) };

        // Paragraph tops inside the band (by in-flow position), measured lazily: only a paragraph that
        // anchors something needs one.
        double ParagraphTop(ModelParagraph paragraph)
        {
            var index = -1;
            for (var i = 0; i < inFlow.Count; i++)
            {
                if (ReferenceEquals(inFlow[i], paragraph))
                {
                    index = i;
                    break;
                }
            }

            return index <= 0
                ? 0
                : MeasureFlowHeight(
                    BuildFlow(model, inFlow.Take(index).ToList(), null, isHeader, context, widthDip, null),
                    widthDip,
                    maxHeightDip);
        }

        // DrawingML anchors (wp:anchor pictures, shapes, charts...) in the slot's own paragraphs.
        var surface = FreeW.App.Presentation.DocumentView.DocumentViewLayoutPlanner.BuildFloatingOverlaySurfacePlan(
            page, printLayout: true, plainInsetDip: 0);
        var snapshots = new List<(DocumentFloatingObjectSnapshot Snapshot, FreeW.Core.Model.Run Run)>();
        for (var paragraphIndex = 0; paragraphIndex < slot.Paragraphs.Count; paragraphIndex++)
        {
            var paragraph = slot.Paragraphs[paragraphIndex];
            if (paragraph.LegacyTextBoxFrame is not null || !paragraph.Runs.Any(IsFloatingRun))
                continue;

            var anchorContentY = bandOriginDip.Y + ParagraphTop(paragraph) - surface.MarginTopDip;
            foreach (var snapshot in FreeW.App.Presentation.DocumentView.DocumentViewLayoutPlanner.BuildFloatingObjectSnapshots(
                         paragraph, paragraphIndex, anchorContentY, surface, columnCount: 1))
            {
                snapshots.Add((snapshot, paragraph.Runs[snapshot.RunIndex]));
            }
        }

        foreach (var behindText in new[] { true, false })
        {
            var ordered = snapshots
                .Where(entry => entry.Snapshot.BehindText == behindText)
                .OrderBy(entry => entry.Snapshot.ZOrderIndex)
                .ThenBy(entry => entry.Snapshot.BlockIndex)
                .ThenBy(entry => entry.Snapshot.RunIndex)
                .ToList();
            if (ordered.Count == 0)
                continue;

            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen())
            {
                foreach (var (snapshot, run) in ordered)
                {
                    if (!TryDrawConnectorLine(dc, snapshot, run))
                        HeaderFooterPaginator.DrawFloatingRun(dc, snapshot, run);
                }
            }

            (behindText ? behind : front).Children.Add(drawing);
        }

        // Positioned legacy VML text boxes: consecutive paragraphs share one frame instance.
        var groups = new List<(LegacyTextBoxFrame Frame, List<ModelParagraph> Paragraphs)>();
        foreach (var paragraph in slot.Paragraphs)
        {
            if (paragraph.LegacyTextBoxFrame is not { } frame)
                continue;
            if (groups.Count > 0 && ReferenceEquals(groups[^1].Frame, frame))
                groups[^1].Paragraphs.Add(paragraph);
            else
                groups.Add((frame, [paragraph]));
        }

        foreach (var (frame, paragraphs) in groups)
        {
            var rect = ResolveLegacyFrameRect(frame, page, bandOriginDip.Y, isHeader ? null : bandOriginDip.Y);
            if (rect.Width <= 0 || rect.Height <= 0)
                continue;

            var frameVisual = new ContainerVisual();
            var shape = new DrawingVisual();
            using (var dc = shape.RenderOpen())
            {
                var fill = BrushFromHex(frame.FillColorHex);
                var pen = BrushFromHex(frame.StrokeColorHex) is { } strokeBrush
                    ? new Pen(strokeBrush, Math.Max(0.5, PageLayout.PointsToDip(frame.StrokeWidthPt)))
                    : null;
                if (fill is not null || pen is not null)
                {
                    if (frame.IsEllipse)
                        dc.DrawEllipse(fill, pen, new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), rect.Width / 2, rect.Height / 2);
                    else
                        dc.DrawRectangle(fill, pen, rect);
                }
            }

            frameVisual.Children.Add(shape);

            var insetLeft = PageLayout.PointsToDip(frame.InsetLeftPt);
            var insetTop = PageLayout.PointsToDip(frame.InsetTopPt);
            var innerWidth = Math.Max(1, rect.Width - insetLeft - PageLayout.PointsToDip(frame.InsetRightPt));
            var innerHeight = Math.Max(1, rect.Height - insetTop - PageLayout.PointsToDip(frame.InsetBottomPt));
            var textHeight = MeasureFlowHeight(
                BuildFlow(model, paragraphs, null, isHeader, context, innerWidth, null),
                innerWidth,
                Math.Max(innerHeight, maxHeightDip));
            var textTop = frame.TextAnchor switch
            {
                LegacyFrameTextAnchor.Middle => rect.Y + insetTop + (innerHeight - textHeight) / 2,
                LegacyFrameTextAnchor.Bottom => rect.Y + insetTop + innerHeight - textHeight,
                _ => rect.Y + insetTop
            };
            var textHost = new ContainerVisual { Offset = new Vector(rect.X + insetLeft, 0) };
            AddFlowPage(
                band,
                textHost,
                BuildFlow(model, paragraphs, null, isHeader, context, innerWidth, null),
                innerWidth,
                Math.Max(1, Math.Max(innerHeight, textHeight) + 1),
                textTop);
            frameVisual.Children.Add(textHost);

            (frame.BehindText ? behind : front).Children.Add(frameVisual);
        }

        if (behind.Children.Count > 0)
            band.Children.Insert(0, behind);
        if (front.Children.Count > 0)
            band.Children.Add(front);
    }

    /// <summary>
    /// A straight connector (<c>prst="line"</c>, e.g. the rule under a header) has a zero-height or
    /// zero-width extent; the snapshot sizes it with the 80pt placeholder default, which would draw a
    /// large box. Draw it as the line it is, in its outline colour and weight.
    /// </summary>
    private static bool TryDrawConnectorLine(DrawingContext dc, DocumentFloatingObjectSnapshot snapshot, FreeW.Core.Model.Run run)
    {
        if (run.Shape is not { } shape || (shape.WidthPt > 0 && shape.HeightPt > 0))
            return false;

        var brush = BrushFromHex(shape.OutlineColorHex ?? "#000000");
        var pen = new Pen(brush, Math.Max(0.75, PageLayout.PointsToDip(shape.OutlineWidthPt > 0 ? shape.OutlineWidthPt : 0.75)));
        var start = new Point(snapshot.Rect.XDip, snapshot.Rect.YDip);
        var end = shape.HeightPt > 0
            ? new Point(start.X, start.Y + PageLayout.PointsToDip(shape.HeightPt))
            : new Point(start.X + PageLayout.PointsToDip(Math.Max(0, shape.WidthPt)), start.Y);
        dc.DrawLine(pen, start, end);
        return true;
    }

    private static bool IsFloatingRun(FreeW.Core.Model.Run run) =>
        run.Image is { IsFloating: true }
        || run.Shape is { IsFloating: true }
        || run.Chart is { IsFloating: true }
        || run.WordArt is { IsFloating: true }
        || run.SmartArt is { IsFloating: true }
        || run.DrawingGroup is not null;

    /// <summary>
    /// The page-space rectangle of a positioned legacy text box: its alignment (or absolute offset)
    /// against the margin box, the page, one of the four margin areas, or -- for "text" -- the band.
    /// </summary>
    internal static Rect ResolveLegacyFrameRect(
        LegacyTextBoxFrame frame,
        PageSettings page,
        double bandTopDip,
        double? footerTopDip = null)
    {
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(page);
        var (marginLeft, marginTop, marginRight, marginBottom) = PageLayout.MarginsDip(page);
        var width = PageLayout.PointsToDip(frame.WidthPt);
        var height = PageLayout.PointsToDip(frame.HeightPt);

        var (left, right) = frame.HorizontalReference switch
        {
            LegacyFrameReference.Page => (0.0, pageWidth),
            LegacyFrameReference.LeftMarginArea => (0.0, marginLeft),
            LegacyFrameReference.RightMarginArea => (pageWidth - marginRight, pageWidth),
            _ => (marginLeft, pageWidth - marginRight)
        };
        var (top, bottom) = frame.VerticalReference switch
        {
            LegacyFrameReference.Page => (0.0, pageHeight),
            LegacyFrameReference.Margin => (marginTop, pageHeight - marginBottom),
            LegacyFrameReference.TopMarginArea => (0.0, marginTop),
            // A footer taller than the bottom margin pushes the body up, and Word's bottom margin area
            // grows with it: it spans from the footer's top (when higher) to the bottom of the sheet.
            LegacyFrameReference.BottomMarginArea => (
                Math.Min(pageHeight - marginBottom, footerTopDip ?? double.MaxValue), pageHeight),
            _ => (bandTopDip, bandTopDip)
        };

        var x = frame.HorizontalAlignment switch
        {
            LegacyFrameHorizontalAlignment.Left => left,
            LegacyFrameHorizontalAlignment.Center => left + (right - left - width) / 2,
            LegacyFrameHorizontalAlignment.Right => right - width,
            _ => left + PageLayout.PointsToDip(frame.LeftPt)
        };
        var y = frame.VerticalAlignment switch
        {
            LegacyFrameVerticalAlignment.Top => top,
            LegacyFrameVerticalAlignment.Center => top + (bottom - top - height) / 2,
            LegacyFrameVerticalAlignment.Bottom => bottom - height,
            _ => top + PageLayout.PointsToDip(frame.TopPt)
        };

        return new Rect(x, y, width, height);
    }

    private static SolidColorBrush? BrushFromHex(string? hex)
    {
        if (string.IsNullOrEmpty(hex))
            return null;
        var brush = new SolidColorBrush(WpfRgbColorAdapter.ParseDrawingMlOrDefault(hex, Colors.Black));
        brush.Freeze();
        return brush;
    }

    private static (List<ModelParagraph> InFlow, List<(ModelParagraph Paragraph, int PrecedingInFlowCount)> Framed) Partition(
        HeaderFooter slot)
    {
        var inFlow = new List<ModelParagraph>();
        var framed = new List<(ModelParagraph, int)>();
        if (slot.Table is not null)
            return (inFlow, framed);

        foreach (var paragraph in slot.Paragraphs)
        {
            // A positioned legacy text box's paragraphs are drawn inside its shape (see
            // AddFloatingObjects), not in the band's flow.
            if (paragraph.LegacyTextBoxFrame is not null)
                continue;
            if (paragraph.FrameHorizontalAlignment is not null && paragraph.Runs.Count > 0)
                framed.Add((paragraph, inFlow.Count));
            else
                inFlow.Add(paragraph);
        }

        return (inFlow, framed);
    }

    private static FlowDocument BuildFlow(
        TextDocument model,
        IReadOnlyList<ModelParagraph> paragraphs,
        FreeW.Core.Model.Table? table,
        bool isHeader,
        HeaderFooterBandContext context,
        double widthDip,
        ModelTextAlignment? forcedAlignment)
    {
        var blocks = new List<FreeW.Core.Model.Block>();
        if (table is { Rows.Count: > 0 })
        {
            // A story cannot end with a table: Word always follows it with a paragraph, which the
            // reader drops as filler but which still takes a line of the header/footer height.
            blocks.Add(table);
            var fillerStyleId = isHeader ? "Header" : "Footer";
            blocks.Add(new ModelParagraph
            {
                StyleId = model.Styles.ContainsKey(fillerStyleId) ? fillerStyleId : null
            });
        }
        else
        {
            blocks.AddRange(paragraphs);
        }

        var sub = PageBox.BuildHfSubEditor(
            model,
            slot: null,
            marginLeft: 0,
            marginRight: 0,
            isHeader,
            isActivated: true,
            hfPageNumber: Math.Max(1, context.PageNumber),
            hfPageNumberText: context.PageNumberText,
            hfPageCount: Math.Max(1, context.PageCount),
            hfSectionOrdinal: Math.Max(1, context.SectionOrdinal),
            hfSectionPageCount: context.SectionPageCount > 0 ? context.SectionPageCount : Math.Max(1, context.PageCount),
            inheritSourceStyles: true,
            blocksOverride: blocks);

        // Take the rendered document away from the (never shown) editor so it can be paginated.
        var flow = sub.Document;
        sub.Document = new FlowDocument();
        flow.PagePadding = new Thickness(0);
        flow.PageWidth = Math.Max(1, widthDip) + TabStopSlackDip;
        flow.ColumnWidth = Math.Max(1, widthDip) + TabStopSlackDip;
        flow.ColumnGap = 0;
        flow.Background = null;

        if (forcedAlignment is { } alignment)
        {
            var wpfAlignment = alignment switch
            {
                ModelTextAlignment.Right => System.Windows.TextAlignment.Right,
                ModelTextAlignment.Center => System.Windows.TextAlignment.Center,
                _ => System.Windows.TextAlignment.Left
            };
            foreach (var block in flow.Blocks)
                block.TextAlignment = wpfAlignment;
        }

        return flow;
    }

    private static bool FitsOnOnePage(FlowDocument flow, double widthDip, double heightDip)
    {
        flow.PageHeight = heightDip;
        var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        paginator.PageSize = new Size(Math.Max(1, widthDip) + TabStopSlackDip, heightDip);
        paginator.ComputePageCount();
        return paginator.PageCount <= 1;
    }

    /// <summary>
    /// The smallest page height at which the flow still paginates to one page — i.e. its laid-out
    /// height including paragraph spacing and trailing empty paragraphs, which ink bounds would miss.
    /// </summary>
    private static double MeasureFlowHeight(FlowDocument flow, double widthDip, double maxHeightDip)
    {
        if (!FitsOnOnePage(flow, widthDip, maxHeightDip))
            return maxHeightDip;

        // WPF always places at least one line on a page, so the content's own last line "fits" at any
        // height. A one-DIP sentinel line after the content is the line whose fit is actually probed;
        // it also keeps the last paragraph's space-after inside the measured height, as Word does.
        var sentinel = new System.Windows.Documents.Paragraph
        {
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            FontSize = 1,
            LineHeight = SentinelLineHeightDip,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight
        };
        flow.Blocks.Add(sentinel);
        try
        {
            var low = 0.0;
            var high = maxHeightDip;
            while (high - low > MeasureToleranceDip)
            {
                var middle = (low + high) / 2;
                if (middle >= 1 && FitsOnOnePage(flow, widthDip, middle))
                    high = middle;
                else
                    low = middle;
            }

            return Math.Max(0, high - SentinelLineHeightDip);
        }
        finally
        {
            flow.Blocks.Remove(sentinel);
        }
    }

    private static void AddFlowPage(
        BandVisual band,
        ContainerVisual parent,
        FlowDocument flow,
        double widthDip,
        double heightDip,
        double topDip)
    {
        flow.PageHeight = heightDip;
        var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        paginator.PageSize = new Size(Math.Max(1, widthDip) + TabStopSlackDip, heightDip);
        paginator.ComputePageCount();
        if (paginator.PageCount <= 0)
            return;

        var page = paginator.GetPage(0);
        if (page == DocumentPage.Missing || page.Visual is null)
            return;

        var host = new ContainerVisual { Offset = new Vector(0, topDip) };
        host.Children.Add(page.Visual);
        parent.Children.Add(host);
        band.Retain(flow, page);
    }

    /// <summary>Keeps the paginated flow and page alive for as long as their visual is on a page.</summary>
    private sealed class BandVisual : ContainerVisual
    {
        private readonly List<object> _retained = [];

        public void Retain(params object[] owners) => _retained.AddRange(owners);
    }
}
