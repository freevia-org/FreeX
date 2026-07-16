using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.RenderCompare;

/// <summary>
/// Headless harness: render every page of each corpus .docx through FreeW's WPF layout paths
/// to a PNG, so its output can be diffed against MS Word's. Single-section documents use the
/// live editor document; documents with section-specific geometry or headers/footers use the
/// section-aware paginated editor path.
///
/// Usage: FreeW.RenderCompare &lt;corpusDir&gt; &lt;outDir&gt; [dpi]
///   corpusDir : folder of .docx files
///   outDir    : folder to write &lt;basename&gt;-p&lt;N&gt;.png and freew-render.csv
///   dpi       : raster DPI (default 150)
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: FreeW.RenderCompare <corpusDir> <outDir> [dpi]");
            return 2;
        }

        var corpusDir = args[0];
        var outDir = args[1];
        var dpi = args.Length >= 3 && double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 150.0;

        Directory.CreateDirectory(outDir);

        // A WPF Application context is needed so resource/font resolution behaves like the live app.
        _ = new Application();

        var docs = Directory.GetFiles(corpusDir, "*.docx").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        var csv = new StringBuilder("file,pages,status,error\n");

        foreach (var path in docs)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            int pages = 0;
            var status = "ok";
            var error = "";
            try
            {
                pages = RenderDocument(path, outDir, name, dpi);
            }
            catch (Exception ex)
            {
                status = "fail";
                error = ex.GetType().Name + ": " + ex.Message.Replace('\n', ' ').Replace('\r', ' ').Replace(',', ';');
                Console.Error.WriteLine($"--- {Path.GetFileName(path)} ---\n{ex}");
            }

            Console.WriteLine($"[{status,-4}] {Path.GetFileName(path),-28} pages={pages}");
            csv.Append($"{Path.GetFileName(path)},{pages},{status},\"{error}\"\n");
        }

        File.WriteAllText(Path.Combine(outDir, "freew-render.csv"), csv.ToString());
        Console.WriteLine($"FreeW render -> {outDir}");
        return 0;
    }

    private static int RenderDocument(string docxPath, string outDir, string baseName, double dpi)
    {
        var model = DocxReader.Read(docxPath);

        var view = new DocumentView();
        view.LoadModel(model);

        if (model.Sections.Count > 1 && NeedsSectionAwareRendering(model))
            return RenderSectionAwareDocument(view, outDir, baseName, dpi);

        // Paginate the editor's *live* FlowDocument directly rather than via PrintLayout.BuildPaginator.
        // BuildPaginator clones blocks with XamlWriter.Save, which throws on the editor's non-public
        // Tag types (ParagraphTag/HyperlinkInfo/Footnote/EndnoteMarker/TableCellTag) — the same crash the
        // app's Print/Print-Preview path hits. We detach the FlowDocument from the (never-shown)
        // RichTextBox so it is not "in use", set the model's page geometry on it exactly as
        // BuildPaginatedDocument does, then wrap with the real HeaderFooterPaginator so headers, footers,
        // watermark, page border and line numbers render identically to the app.
        var flow = view.Document;
        view.Document = new System.Windows.Documents.FlowDocument();

        var pageSettings = view.Model.Page;
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(pageSettings);
        var (left, top, right, bottom) = PageLayout.MarginsDip(pageSettings);
        flow.PageWidth = pageWidth;
        flow.PageHeight = pageHeight;
        flow.PagePadding = new Thickness(left, top, right, bottom);
        DocumentView.ApplyColumnLayout(flow, pageSettings);
        // ApplyColumnLayout sets ColumnWidth = +Infinity for single-column and leans on PageWidth to
        // constrain it; for some header/multi-section docs that leaks through as an unconstrained
        // "paragraphWidth ('∞')" layout failure. Pin the single column to the finite content width
        // (visually identical — one column spanning the content area).
        if (double.IsInfinity(flow.ColumnWidth))
            flow.ColumnWidth = Math.Max(1, pageWidth - left - right);

        var floatingCanvas = new Canvas { Width = pageWidth, Height = pageHeight };
        view.SetFloatingCanvas(floatingCanvas);
        var floatingVisual = RasterizeFloatingObjects(floatingCanvas, pageWidth, pageHeight);

        var inner = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        inner.PageSize = new Size(pageWidth, pageHeight);
        var lineHeightDip = flow.FontSize * (4.0 / 3.0);
        var paginator = new HeaderFooterPaginator(inner, view.Model, pageSettings, lineHeightDip);
        paginator.ComputePageCount();
        var pageCount = paginator.PageCount;

        var scale = dpi / 96.0;

        for (var i = 0; i < pageCount; i++)
        {
            DocumentPage page;
            try
            {
                page = paginator.GetPage(i);
            }
            catch (Exception ex)
            {
                // One bad page (e.g. an undecodable embedded image) should not sink the whole document.
                Console.Error.WriteLine($"  page {i + 1}/{pageCount} of {baseName} failed: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            var size = page.Size;
            var pxW = Math.Max(1, (int)Math.Ceiling(size.Width * scale));
            var pxH = Math.Max(1, (int)Math.Ceiling(size.Height * scale));

            // Composite the page over an opaque white sheet so transparent areas read as paper, not black.
            var container = new ContainerVisual();
            var bg = new DrawingVisual();
            using (var dc = bg.RenderOpen())
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, size.Width, size.Height));
            container.Children.Add(bg);
            container.Children.Add(page.Visual);
            if (floatingVisual is not null)
                container.Children.Add(floatingVisual);

            var rtb = new RenderTargetBitmap(pxW, pxH, dpi, dpi, PixelFormats.Pbgra32);
            rtb.Render(container);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            var outPath = Path.Combine(outDir, $"{baseName}-p{i + 1}.png");
            using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write);
            encoder.Save(fs);
        }

        return pageCount;
    }

    private static DrawingVisual? RasterizeFloatingObjects(Canvas canvas, double pageWidth, double pageHeight)
    {
        canvas.Measure(new Size(pageWidth, pageHeight));
        canvas.Arrange(new Rect(0, 0, pageWidth, pageHeight));
        canvas.UpdateLayout();
        if (canvas.Children.Count == 0)
            return null;

        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();
        foreach (UIElement child in canvas.Children)
        {
            var left = Canvas.GetLeft(child);
            var top = Canvas.GetTop(child);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;

            if (child is Image image && image.Source is ImageSource source)
            {
                var width = image.Width;
                var height = image.Height;
                if (!double.IsNaN(width) && !double.IsNaN(height) && width > 0 && height > 0)
                    dc.DrawImage(source, new Rect(left, top, width, height));
            }
            else if (child is FrameworkElement element
                && element.ActualWidth > 0
                && element.ActualHeight > 0)
            {
                dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.Fill }, null,
                    new Rect(left, top, element.ActualWidth, element.ActualHeight));
            }
        }

        return visual;
    }

    private static int RenderSectionAwareDocument(DocumentView sourceView, string outDir, string baseName, double dpi)
    {
        var panel = PaginatedEditorPanel.Build(sourceView);
        var pageBoxes = panel.PageBoxes.Where(box => !box.IsEndnoteSyntheticPage).ToList();
        var pageCount = Math.Max(1, pageBoxes.Count);

        for (var index = 0; index < pageBoxes.Count; index++)
        {
            var box = pageBoxes[index];
            var page = box.PageGeometry;
            var (pageWidth, pageHeight) = PageLayout.PageSizeDip(page);
            var (marginLeft, marginTop, marginRight, marginBottom) = PageLayout.MarginsDip(page);
            var (contentWidth, contentHeight) = PageLayout.ContentAreaDip(page);
            var scale = dpi / 96.0;
            var pxW = Math.Max(1, (int)Math.Ceiling(pageWidth * scale));
            var pxH = Math.Max(1, (int)Math.Ceiling(pageHeight * scale));

            var container = new ContainerVisual();
            var background = new DrawingVisual();
            using (var dc = background.RenderOpen())
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageWidth, pageHeight));
            container.Children.Add(background);

            var bodyPage = PaginateBody(box.Body.Document, page, contentWidth, contentHeight);
            if (bodyPage is not null)
            {
                var bodyVisual = new DrawingVisual();
                using (var dc = bodyVisual.RenderOpen())
                {
                    dc.DrawRectangle(new VisualBrush(bodyPage.Visual) { Stretch = Stretch.None }, null,
                        new Rect(marginLeft, marginTop, contentWidth, contentHeight));
                }
                container.Children.Add(bodyVisual);
            }

            var ownerHf = box.OwnerSectionHf ?? sourceView.Model.FinalSectionHeadersFooters;
            var headerSlot = ResolveHfSlot(ownerHf, box.HeaderSlotName);
            AddHeaderFooterVisual(container, headerSlot, sourceView.Model, pageWidth, pageHeight,
                marginLeft, marginTop, marginRight, marginBottom, index + 1, pageCount, isHeader: true);
            AddHeaderFooterVisual(container, ResolveHfSlot(ownerHf, box.FooterSlotName), sourceView.Model, pageWidth, pageHeight,
                marginLeft, marginTop, marginRight, marginBottom, index + 1, pageCount, isHeader: false);

            var bitmap = new RenderTargetBitmap(pxW, pxH, dpi, dpi, PixelFormats.Pbgra32);
            bitmap.Render(container);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var outPath = Path.Combine(outDir, $"{baseName}-p{index + 1}.png");
            using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write);
            encoder.Save(fs);
        }

        return pageCount;
    }

    private static HeaderFooter? ResolveHfSlot(SectionHeadersFooters headersFooters, string? slotName) =>
        slotName switch
        {
            "header" => headersFooters.Header,
            "footer" => headersFooters.Footer,
            "even-header" => headersFooters.EvenHeader,
            "even-footer" => headersFooters.EvenFooter,
            "first-header" => headersFooters.FirstHeader,
            "first-footer" => headersFooters.FirstFooter,
            _ => null,
        };

    private static bool NeedsSectionAwareRendering(TextDocument document) =>
        document.Sections.Any(section =>
            !section.HeadersFooters.IsEmpty || PageGeometryDiffers(section.Page, document.Page));

    private static bool PageGeometryDiffers(PageSettings left, PageSettings right) =>
        left.WidthPt != right.WidthPt
        || left.HeightPt != right.HeightPt
        || left.MarginLeftPt != right.MarginLeftPt
        || left.MarginRightPt != right.MarginRightPt
        || left.MarginTopPt != right.MarginTopPt
        || left.MarginBottomPt != right.MarginBottomPt
        || left.Landscape != right.Landscape
        || left.GutterPt != right.GutterPt
        || left.HeaderDistancePt != right.HeaderDistancePt
        || left.FooterDistancePt != right.FooterDistancePt
        || left.ColumnCount != right.ColumnCount
        || left.ColumnSpacingPt != right.ColumnSpacingPt
        || left.ColumnsLineBetween != right.ColumnsLineBetween
        || !SequenceEqual(left.ColumnWidthsPt, right.ColumnWidthsPt);

    private static bool SequenceEqual(IReadOnlyList<double>? left, IReadOnlyList<double>? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;
        return left.SequenceEqual(right);
    }

    private static DocumentPage? PaginateBody(
        FlowDocument body,
        PageSettings page,
        double contentWidth,
        double contentHeight)
    {
        body.PageWidth = Math.Max(1, contentWidth);
        body.PageHeight = Math.Max(1, contentHeight);
        body.PagePadding = new Thickness(0);
        DocumentView.ApplyColumnLayout(body, page);
        var paginator = ((IDocumentPaginatorSource)body).DocumentPaginator;
        paginator.PageSize = new Size(Math.Max(1, contentWidth), Math.Max(1, contentHeight));
        paginator.ComputePageCount();
        return paginator.PageCount > 0 ? paginator.GetPage(0) : null;
    }

    private static void AddHeaderFooterVisual(
        ContainerVisual container,
        HeaderFooter? slot,
        TextDocument sourceModel,
        double pageWidth,
        double pageHeight,
        double marginLeft,
        double marginTop,
        double marginRight,
        double marginBottom,
        int pageNumber,
        int pageCount,
        bool isHeader)
    {
        if (slot is null || slot.IsEmpty)
            return;

        const double stripHeight = 36;
        var page = RenderHfSlot(slot, sourceModel, pageWidth, stripHeight, pageNumber, pageCount);
        if (page is null)
            return;

        var visual = new DrawingVisual();
        var y = isHeader
            ? Math.Max(0, marginTop - stripHeight)
            : pageHeight - Math.Max(18, marginBottom - 18);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new VisualBrush(page.Visual)
            {
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            }, null, new Rect(marginLeft, y, pageWidth - marginLeft - marginRight, stripHeight));
        }
        container.Children.Add(visual);
    }

    private static DocumentPage? RenderHfSlot(
        HeaderFooter slot,
        TextDocument sourceModel,
        double pageWidth,
        double height,
        int pageNumber,
        int pageCount)
    {
        var wrapper = TextDocument.CreateEmpty();
        wrapper.DefaultRun = sourceModel.DefaultRun;
        wrapper.DefaultParagraph = sourceModel.DefaultParagraph;
        wrapper.Blocks.Clear();
        foreach (var paragraph in slot.Paragraphs)
            wrapper.Blocks.Add(paragraph);
        if (wrapper.Blocks.Count == 0)
            return null;

        var view = new DocumentView { Width = pageWidth };
        DocumentView._renderHfPageNumber = pageNumber;
        DocumentView._renderHfPageCount = pageCount;
        try
        {
            view.LoadModel(wrapper);
        }
        finally
        {
            DocumentView._renderHfPageNumber = 0;
            DocumentView._renderHfPageCount = 0;
        }

        var flow = view.Document;
        view.Document = new FlowDocument();
        flow.PageWidth = pageWidth;
        flow.PageHeight = height;
        flow.PagePadding = new Thickness(0);
        flow.ColumnWidth = double.PositiveInfinity;
        var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        paginator.PageSize = new Size(pageWidth, height);
        paginator.ComputePageCount();
        return paginator.PageCount > 0 ? paginator.GetPage(0) : null;
    }
}
