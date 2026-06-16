using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A modeless, read-only print-preview window. It paginates the editor's current content into
/// discrete pages at the model's <see cref="PageSettings"/> size and margins, so the user sees the
/// real page boundaries that printing will produce.
///
/// Pagination is delegated to WPF's <see cref="FlowDocumentPageViewer"/>: by setting the previewed
/// <see cref="FlowDocument"/>'s <see cref="FlowDocument.PageWidth"/>/<see cref="FlowDocument.PageHeight"/>
/// (and a single-column layout) to the page geometry computed by <see cref="PageLayout"/>, the
/// viewer's internal <see cref="DocumentPaginator"/> breaks the flow into page-sized pieces. The
/// document header/footer (with a live page number) are drawn into each page's top/bottom margin by
/// a paginator decorator, so they appear on every previewed and printed page.
/// This window never edits the model; it works on a display-only copy of the editor's FlowDocument so
/// the concurrent model/FlowDocument mapping in <see cref="DocumentView"/> is untouched.
/// </summary>
public sealed class PrintPreviewWindow : Window
{
    public PrintPreviewWindow(DocumentView editor)
    {
        Title = "Print Preview — FreeW";
        Width = 900;
        Height = 760;
        Background = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
        Owner = Window.GetWindow(editor);

        var viewer = new DocumentViewer
        {
            Document = PrintLayout.BuildPaginatedSource(editor)
        };

        Content = viewer;
    }
}

/// <summary>
/// Shared layout/printing helper. Builds a page-settings-aware paginated source from the editor's
/// current content (used by both the print-preview window and <see cref="MainWindow.Print"/>),
/// converting the model's point-based <see cref="PageSettings"/> into DIP via <see cref="PageLayout"/>
/// and overlaying the document header/footer (with a live page number) on each page.
/// </summary>
internal static class PrintLayout
{
    /// <summary>
    /// Produces a fresh <see cref="FlowDocument"/> whose page size and margins match the model's
    /// <see cref="PageSettings"/>, carrying a display-only clone of the editor's content. The clone is
    /// taken via XAML round-tripping over the editor's FlowDocument so this path never reaches into
    /// the model&lt;-&gt;FlowDocument mapping owned by <see cref="DocumentView"/>.
    /// </summary>
    public static FlowDocument BuildPaginatedDocument(DocumentView editor)
    {
        var page = editor.Model.Page;
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(page);
        var (left, top, right, bottom) = PageLayout.MarginsDip(page);

        var flow = new FlowDocument
        {
            PageWidth = pageWidth,
            PageHeight = pageHeight,
            PagePadding = new Thickness(left, top, right, bottom),
            FontFamily = editor.Document.FontFamily,
            FontSize = editor.Document.FontSize
        };

        // Mirror the editor's multi-column layout so preview/print match the on-screen rendering.
        DocumentView.ApplyColumnLayout(flow, page);

        foreach (var block in CloneBlocks(editor.Document))
            flow.Blocks.Add(block);

        return flow;
    }

    /// <summary>
    /// Builds a paginator for the editor content at the model's page geometry, wrapped so the document
    /// header and footer (with a live page number) are drawn into each page's margin areas. Used by
    /// both the preview window and the print path so the two stay in sync.
    /// </summary>
    public static DocumentPaginator BuildPaginator(DocumentView editor)
    {
        var page = editor.Model.Page;
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(page);

        var flow = BuildPaginatedDocument(editor);
        var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        paginator.PageSize = new Size(pageWidth, pageHeight);

        return new HeaderFooterPaginator(paginator, editor.Model, page);
    }

    /// <summary>
    /// A <see cref="DocumentViewer"/>-friendly paginator source wrapping <see cref="BuildPaginator"/>,
    /// for the print-preview window (which binds an <see cref="IDocumentPaginatorSource"/>).
    /// </summary>
    public static IDocumentPaginatorSource BuildPaginatedSource(DocumentView editor) =>
        new PaginatorSource(BuildPaginator(editor));

    /// <summary>
    /// Deep-clones the editor FlowDocument's blocks via XAML serialization. We clone (rather than
    /// re-host the live FlowDocument) because a FlowDocument may belong to only one container at a
    /// time, and the editor keeps its own; cloning leaves the editable surface untouched.
    /// </summary>
    private static IEnumerable<System.Windows.Documents.Block> CloneBlocks(FlowDocument source)
    {
        var clone = (FlowDocument)CloneElement(source);
        // Detach blocks from the clone so they can be re-parented into the target FlowDocument.
        var blocks = clone.Blocks.ToList();
        clone.Blocks.Clear();
        return blocks;
    }

    private static object CloneElement(object element)
    {
        var xaml = XamlWriter.Save(element);
        using var reader = new StringReader(xaml);
        using var xmlReader = System.Xml.XmlReader.Create(reader);
        return XamlReader.Load(xmlReader);
    }

    /// <summary>Minimal <see cref="IDocumentPaginatorSource"/> exposing a ready-made paginator.</summary>
    private sealed class PaginatorSource(DocumentPaginator paginator) : IDocumentPaginatorSource
    {
        public DocumentPaginator DocumentPaginator { get; } = paginator;
    }
}

/// <summary>
/// Wraps an inner <see cref="DocumentPaginator"/> and composites the document header into the top
/// margin and footer into the bottom margin of every produced page. A page-number field in the
/// header/footer is rendered with the live 1-based page number for that page.
/// </summary>
internal sealed class HeaderFooterPaginator(DocumentPaginator inner, TextDocument model, PageSettings page) : DocumentPaginator
{
    public override bool IsPageCountValid => inner.IsPageCountValid;
    public override int PageCount => inner.PageCount;
    public override Size PageSize { get => inner.PageSize; set => inner.PageSize = value; }
    public override IDocumentPaginatorSource Source => inner.Source;

    public override DocumentPage GetPage(int pageNumber)
    {
        var basePage = inner.GetPage(pageNumber);
        var hasWatermark = !string.IsNullOrEmpty(page.Watermark);
        var hasBorder = page.PageBorder is not null;
        if (model.Header is not { IsEmpty: false } && model.Footer is not { IsEmpty: false }
            && !hasWatermark && !hasBorder)
            return basePage;

        var size = basePage.Size;
        var visual = new ContainerVisual();
        // The watermark sits behind the page content; the border is drawn on top of the page edge.
        if (hasWatermark)
            visual.Children.Add(BuildWatermark(page.Watermark!, size));
        visual.Children.Add(basePage.Visual);
        if (hasBorder)
            visual.Children.Add(BuildPageBorder(page.PageBorder!, size));

        var marginLeft = PageLayout.PointsToDip(page.MarginLeftPt);
        var contentWidth = Math.Max(0, size.Width - marginLeft - PageLayout.PointsToDip(page.MarginRightPt));

        if (model.Header is { IsEmpty: false } header)
        {
            var headerText = ResolveText(header, pageNumber);
            var top = PageLayout.PointsToDip(Math.Max(0, page.MarginTopPt - 36));
            visual.Children.Add(BuildOverlay(headerText, marginLeft, top, contentWidth));
        }

        if (model.Footer is { IsEmpty: false } footer)
        {
            var footerText = ResolveText(footer, pageNumber);
            var bottom = size.Height - PageLayout.PointsToDip(Math.Max(18, page.MarginBottomPt - 18));
            visual.Children.Add(BuildOverlay(footerText, marginLeft, bottom, contentWidth));
        }

        return new DocumentPage(visual, size, basePage.BleedBox, basePage.ContentBox);
    }

    /// <summary>
    /// Draws the whole-page border (w:pgBorders) as a rectangle just inside the page edge, matching the
    /// editor's BorderBrush/BorderThickness chrome so on-screen and printed pages agree.
    /// </summary>
    private static DrawingVisual BuildPageBorder(PageBorder border, Size size)
    {
        var visual = new DrawingVisual();
        var color = ParseColor(border.ColorHex);
        var thickness = Math.Max(1, PageLayout.PointsToDip(border.WidthPt));
        var pen = new Pen(new SolidColorBrush(color), thickness);
        // Inset by half the stroke width plus the 24pt offsetFrom="page" gap used on save, clamped so
        // the rectangle stays positive on small pages.
        var inset = thickness / 2 + Math.Min(PageLayout.PointsToDip(24), Math.Min(size.Width, size.Height) / 4);
        var rect = new Rect(inset, inset, Math.Max(0, size.Width - 2 * inset), Math.Max(0, size.Height - 2 * inset));
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(null, pen, rect);
        return visual;
    }

    /// <summary>Draws the faint, 45-degree page watermark text centred on the page, behind the content.</summary>
    private static DrawingVisual BuildWatermark(string text, Size size)
    {
        var visual = new DrawingVisual();
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Calibri"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            PageLayout.PointsToDip(48.0),
            new SolidColorBrush(Color.FromArgb(0x28, 0x80, 0x80, 0x80)),
            1.0);

        var center = new Point(size.Width / 2, size.Height / 2);
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new RotateTransform(-45, center.X, center.Y));
            dc.DrawText(formatted, new Point(center.X - formatted.Width / 2, center.Y - formatted.Height / 2));
            dc.Pop();
        }
        return visual;
    }

    private static Color ParseColor(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return Colors.Black;
        }
    }

    /// <summary>Renders one line of header/footer text into a positioned drawing visual.</summary>
    private static DrawingVisual BuildOverlay(string text, double x, double y, double width)
    {
        var visual = new DrawingVisual();
        if (string.IsNullOrEmpty(text))
            return visual;

        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Calibri"),
            PageLayout.PointsToDip(11.0), // 11pt header/footer text, expressed in DIP
            Brushes.Black,
            1.0)
        {
            MaxTextWidth = width > 0 ? width : double.PositiveInfinity,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };

        using (var dc = visual.RenderOpen())
            dc.DrawText(formatted, new Point(x, y));
        return visual;
    }

    /// <summary>
    /// Flattens a header/footer to a single display line, substituting the live page number for any
    /// page-number field run.
    /// </summary>
    private string ResolveText(HeaderFooter content, int zeroBasedPageNumber)
    {
        var displayPage = (zeroBasedPageNumber + 1).ToString(System.Globalization.CultureInfo.CurrentCulture);
        var lines = content.Paragraphs.Select(p =>
            string.Concat(p.Runs.Select(r =>
                r.FieldKind == RunFieldKind.PageNumber ? displayPage : r.Text)));
        return string.Join("  ", lines.Where(l => l.Length > 0));
    }
}
