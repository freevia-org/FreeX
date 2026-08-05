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
/// Paginates documents whose sections do not share one page geometry. WPF's normal
/// <see cref="FlowDocument"/> paginator has one <see cref="DocumentPaginator.PageSize"/> for the
/// whole flow, so it cannot represent a portrait page followed by a landscape page. This paginator
/// uses the existing section-aware page sharding engine and exposes one physical page per page box.
/// </summary>
internal sealed class SectionAwareDocumentPaginator : DocumentPaginator
{
    private const double HeaderFooterStripHeightDip = 36;

    private readonly IReadOnlyList<PageBox> _pageBoxes;
    private readonly IDocumentPaginatorSource _source;
    private Size _pageSize;

    private SectionAwareDocumentPaginator(DocumentView sourceEditor, PaginatedEditorPanel panel)
    {
        _pageBoxes = panel.PageBoxes.ToArray();
        var (width, height) = PageLayout.PageSizeDip(sourceEditor.Model.Page);
        _pageSize = new Size(width, height);
        _source = new PaginatorSource(this);
    }

    internal static DocumentPaginator Build(DocumentView sourceEditor)
    {
        ArgumentNullException.ThrowIfNull(sourceEditor);
        return new SectionAwareDocumentPaginator(
            sourceEditor,
            PaginatedEditorPanel.Build(sourceEditor, includeParityBlankPages: true));
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
        var (marginLeft, marginTop, marginRight, marginBottom) = PageLayout.MarginsDip(page);
        var (contentWidth, contentHeight) = PageLayout.ContentAreaDip(page);

        var visual = new ContainerVisual();
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageWidth, pageHeight));
        visual.Children.Add(background);

        var bodyPage = PaginateFlow(box.Body.Document, page, contentWidth, contentHeight);
        if (bodyPage is not null)
        {
            var bodyVisual = new DrawingVisual();
            using (var dc = bodyVisual.RenderOpen())
            {
                dc.DrawRectangle(
                    new VisualBrush(bodyPage.Visual) { Stretch = Stretch.None },
                    null,
                    new Rect(marginLeft, marginTop, contentWidth, contentHeight));
            }
            visual.Children.Add(bodyVisual);
        }

        if (page.ColumnsLineBetween && page.ColumnCount > 1)
            visual.Children.Add(DocumentView.BuildColumnRuleVisual(
                page,
                marginLeft,
                marginTop,
                contentWidth,
                pageHeight - marginBottom));

        AddHeaderFooterVisual(
            visual,
            box.HeaderSubEditor,
            pageWidth,
            pageHeight,
            marginLeft,
            marginTop,
            marginRight,
            marginBottom,
            isHeader: true);
        AddHeaderFooterVisual(
            visual,
            box.FooterSubEditor,
            pageWidth,
            pageHeight,
            marginLeft,
            marginTop,
            marginRight,
            marginBottom,
            isHeader: false);

        return new DocumentPage(
            visual,
            new Size(pageWidth, pageHeight),
            new Rect(0, 0, pageWidth, pageHeight),
            new Rect(marginLeft, marginTop, contentWidth, contentHeight));
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

    private static void AddHeaderFooterVisual(
        ContainerVisual container,
        DocumentView? editor,
        double pageWidth,
        double pageHeight,
        double marginLeft,
        double marginTop,
        double marginRight,
        double marginBottom,
        bool isHeader)
    {
        if (editor is null)
            return;

        var page = PaginateHeaderFooter(editor.Document, pageWidth);
        if (page is null)
            return;

        var visual = new DrawingVisual();
        var y = isHeader
            ? Math.Max(0, marginTop - HeaderFooterStripHeightDip)
            : pageHeight - Math.Max(18, marginBottom - 18);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(
                new VisualBrush(page.Visual)
                {
                    Stretch = Stretch.Fill,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top
                },
                null,
                new Rect(
                    marginLeft,
                    y,
                    Math.Max(0, pageWidth - marginLeft - marginRight),
                    HeaderFooterStripHeightDip));
        }
        container.Children.Add(visual);
    }

    private static DocumentPage? PaginateHeaderFooter(FlowDocument flow, double pageWidth)
    {
        flow.PageWidth = Math.Max(1, pageWidth);
        flow.PageHeight = HeaderFooterStripHeightDip;
        flow.PagePadding = new Thickness(0);
        flow.ColumnWidth = Math.Max(1, pageWidth);

        var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        paginator.PageSize = new Size(Math.Max(1, pageWidth), HeaderFooterStripHeightDip);
        paginator.ComputePageCount();
        return paginator.PageCount > 0 ? paginator.GetPage(0) : null;
    }

    private sealed class PaginatorSource(DocumentPaginator paginator) : IDocumentPaginatorSource
    {
        public DocumentPaginator DocumentPaginator { get; } = paginator;
    }
}
