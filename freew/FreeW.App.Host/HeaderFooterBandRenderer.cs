using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
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
        double maxHeightDip)
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
                BuildFlow(model, [paragraph], null, isHeader, context, widthDip, paragraph.FrameHorizontalAlignment),
                widthDip,
                Math.Max(1, maxHeightDip - top),
                top);
        }

        return band.Children.Count == 0 ? null : band;
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

    private static void AddFlowPage(BandVisual band, FlowDocument flow, double widthDip, double heightDip, double topDip)
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
        band.Children.Add(host);
        band.Retain(flow, page);
    }

    /// <summary>Keeps the paginated flow and page alive for as long as their visual is on a page.</summary>
    private sealed class BandVisual : ContainerVisual
    {
        private readonly List<object> _retained = [];

        public void Retain(params object[] owners) => _retained.AddRange(owners);
    }
}
