using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.Core.Model;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace FreeX.App.Host;

internal sealed record PdfBookmark(string Title, int PageIndex);

internal static class PdfDocumentExporter
{
    private const double StandardDpi = 96.0;
    private const double MinimumSizeDpi = 72.0;

    public static void Save(
        FixedDocument document,
        string path,
        PdfDocumentProperties? properties = null,
        string pdfLanguage = ExportPlanner.DefaultPdfLanguage)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        SavePages(document, path, properties, firstPageIndex: 0, lastPageIndexInclusive: document.Pages.Count - 1, pdfLanguage: pdfLanguage);
    }

    public static void Save(
        FixedDocument document,
        string path,
        PdfDocumentProperties? properties,
        ExportPageRange? pageRange,
        ExportQuality quality = ExportQuality.Standard,
        IReadOnlyList<PdfBookmark>? bookmarks = null,
        PdfInitialView initialView = PdfInitialView.SinglePage,
        PdfOpenMode openMode = PdfOpenMode.Normal,
        bool includeSelectableText = false,
        string pdfLanguage = ExportPlanner.DefaultPdfLanguage)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!ExportPlanner.TryValidatePageRange(pageRange, document.Pages.Count, out var pageRangeError))
            throw new InvalidOperationException(pageRangeError);

        var firstPageIndex = Math.Max(0, (pageRange?.FromPage ?? 1) - 1);
        var lastPageIndexInclusive = Math.Min(document.Pages.Count - 1, (pageRange?.ToPage ?? document.Pages.Count) - 1);
        SavePages(document, path, properties, firstPageIndex, lastPageIndexInclusive, ResolveRasterDpi(quality), bookmarks, initialView, openMode, includeSelectableText, pdfLanguage);
    }

    internal static double ResolveRasterDpi(ExportQuality quality) =>
        quality == ExportQuality.MinimumSize
            ? MinimumSizeDpi
            : StandardDpi;

    private static void SavePages(
        FixedDocument document,
        string path,
        PdfDocumentProperties? properties,
        int firstPageIndex,
        int lastPageIndexInclusive,
        double dpi = StandardDpi,
        IReadOnlyList<PdfBookmark>? bookmarks = null,
        PdfInitialView initialView = PdfInitialView.SinglePage,
        PdfOpenMode openMode = PdfOpenMode.Normal,
        bool includeSelectableText = false,
        string pdfLanguage = ExportPlanner.DefaultPdfLanguage)
    {
        if (firstPageIndex > lastPageIndexInclusive || document.Pages.Count == 0)
            throw new InvalidOperationException("The requested page range does not contain any exportable pages.");

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var pdf = new PdfDocument();
        if (includeSelectableText)
            pdf.Options.CompressContentStreams = false;
        pdf.Info.Creator = "FreeX";
        ApplyDefaultCatalogMetadata(pdf, pdfLanguage);
        ApplyDefaultViewerPreferences(pdf, initialView);
        ApplyProperties(pdf, properties);

        for (int i = firstPageIndex; i <= lastPageIndexInclusive; i++)
        {
            var fixedPage = GetFixedPage(document.Pages[i]);
            var pageSize = GetPageSize(document, fixedPage);
            fixedPage.Measure(pageSize);
            fixedPage.Arrange(new Rect(pageSize));
            fixedPage.UpdateLayout();

            var bitmap = RenderPage(fixedPage, pageSize, dpi);
            var page = pdf.AddPage();
            page.Width = XUnit.FromPoint(pageSize.Width * 72.0 / StandardDpi);
            page.Height = XUnit.FromPoint(pageSize.Height * 72.0 / StandardDpi);

            using var gfx = XGraphics.FromPdfPage(page);
            using var image = XImage.FromBitmapSource(bitmap);
            gfx.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
            DrawVectorOverlays(gfx, fixedPage);
            if (includeSelectableText)
                DrawTextOverlay(gfx, fixedPage);
            AddLinkAnnotations(page, fixedPage);
        }

        var hasBookmarks = AddBookmarks(pdf, bookmarks, firstPageIndex, lastPageIndexInclusive);
        ApplyOpenMode(pdf, openMode, hasBookmarks);
        pdf.Save(path);
    }

    private static bool AddBookmarks(
        PdfDocument pdf,
        IReadOnlyList<PdfBookmark>? bookmarks,
        int firstPageIndex,
        int lastPageIndexInclusive)
    {
        if (bookmarks is null || bookmarks.Count == 0)
            return false;

        foreach (var bookmark in bookmarks)
        {
            if (string.IsNullOrWhiteSpace(bookmark.Title) ||
                bookmark.PageIndex < firstPageIndex ||
                bookmark.PageIndex > lastPageIndexInclusive)
            {
                continue;
            }

            var exportedPageIndex = bookmark.PageIndex - firstPageIndex;
            if (exportedPageIndex < 0 || exportedPageIndex >= pdf.Pages.Count)
                continue;

            pdf.Outlines.Add(bookmark.Title.Trim(), pdf.Pages[exportedPageIndex], opened: false);
        }

        if (pdf.Outlines.Count > 0)
        {
            pdf.Internals.Catalog.Elements.SetName("/NonFullScreenPageMode", "/UseOutlines");
            return true;
        }

        return false;
    }

    private static void ApplyProperties(PdfDocument pdf, PdfDocumentProperties? properties)
    {
        if (properties is null)
            return;

        if (NormalizeProperty(properties.Title) is { } title)
        {
            pdf.Info.Title = title;
            SetDisplayDocumentTitlePreference(pdf);
        }
        if (NormalizeProperty(properties.Author) is { } author)
            pdf.Info.Author = author;
        if (NormalizeProperty(properties.Subject) is { } subject)
            pdf.Info.Subject = subject;
        if (NormalizeProperty(properties.Keywords) is { } keywords)
            pdf.Info.Keywords = keywords;
    }

    private static string? NormalizeProperty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ApplyDefaultCatalogMetadata(PdfDocument pdf, string? pdfLanguage)
    {
        pdf.Internals.Catalog.Elements.SetString("/Lang", ExportPlanner.NormalizePdfLanguage(pdfLanguage));
    }

    private static void SetDisplayDocumentTitlePreference(PdfDocument pdf)
    {
        const string displayDocTitleKey = "/DisplayDocTitle";

        GetOrCreateViewerPreferences(pdf).Elements.SetBoolean(displayDocTitleKey, true);
    }

    private static void ApplyDefaultViewerPreferences(PdfDocument pdf, PdfInitialView initialView)
    {
        const string printScalingKey = "/PrintScaling";
        const string noPrintScalingName = "/None";
        const string fitWindowKey = "/FitWindow";
        const string centerWindowKey = "/CenterWindow";
        const string pickTrayByPdfSizeKey = "/PickTrayByPDFSize";

        pdf.PageLayout = initialView switch
        {
            PdfInitialView.OneColumn => PdfPageLayout.OneColumn,
            PdfInitialView.TwoColumnLeft => PdfPageLayout.TwoColumnLeft,
            PdfInitialView.TwoColumnRight => PdfPageLayout.TwoColumnRight,
            _ => PdfPageLayout.SinglePage
        };
        var viewerPreferences = GetOrCreateViewerPreferences(pdf);
        viewerPreferences.Elements.SetName(printScalingKey, noPrintScalingName);
        viewerPreferences.Elements.SetBoolean(fitWindowKey, true);
        viewerPreferences.Elements.SetBoolean(centerWindowKey, true);
        viewerPreferences.Elements.SetBoolean(pickTrayByPdfSizeKey, true);
    }

    private static void ApplyOpenMode(PdfDocument pdf, PdfOpenMode openMode, bool hasBookmarks)
    {
        pdf.PageMode = openMode switch
        {
            PdfOpenMode.FullScreen => PdfPageMode.FullScreen,
            PdfOpenMode.Outlines => PdfPageMode.UseOutlines,
            _ when hasBookmarks => PdfPageMode.UseOutlines,
            _ => PdfPageMode.UseNone
        };
    }

    private static PdfDictionary GetOrCreateViewerPreferences(PdfDocument pdf)
    {
        const string viewerPreferencesKey = "/ViewerPreferences";

        var viewerPreferences = pdf.Internals.Catalog.Elements.GetDictionary(viewerPreferencesKey);
        if (viewerPreferences is null)
        {
            viewerPreferences = new PdfDictionary(pdf);
            pdf.Internals.Catalog.Elements[viewerPreferencesKey] = viewerPreferences;
        }

        return viewerPreferences;
    }

    private static FixedPage GetFixedPage(PageContent pageContent)
    {
        pageContent.GetPageRoot(forceReload: false);
        return pageContent.Child ??
            throw new InvalidOperationException("FixedDocument page content did not contain a FixedPage.");
    }

    private static Size GetPageSize(FixedDocument document, FixedPage page)
    {
        var width = page.Width;
        if (double.IsNaN(width) || width <= 0)
            width = document.DocumentPaginator.PageSize.Width;

        var height = page.Height;
        if (double.IsNaN(height) || height <= 0)
            height = document.DocumentPaginator.PageSize.Height;

        if (double.IsNaN(width) || width <= 0 || double.IsNaN(height) || height <= 0)
            throw new InvalidOperationException("Cannot export a PDF page without a valid page size.");

        return new Size(width, height);
    }

    private static BitmapSource RenderPage(FixedPage page, Size pageSize, double dpi)
    {
        var scale = dpi / StandardDpi;
        var target = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(pageSize.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(pageSize.Height * scale)),
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        target.Render(page);
        target.Freeze();
        return target;
    }

    private static void DrawTextOverlay(XGraphics gfx, FixedPage page)
    {
        foreach (var overlay in PdfTextOverlayExtractor.Extract(page))
        {
            var style = XFontStyleEx.Regular;
            if (overlay.Bold && overlay.Italic)
                style = XFontStyleEx.BoldItalic;
            else if (overlay.Bold)
                style = XFontStyleEx.Bold;
            else if (overlay.Italic)
                style = XFontStyleEx.Italic;

            var font = new XFont(overlay.FontFamily, overlay.FontSize * 72.0 / StandardDpi, style);
            var brush = new XSolidBrush(XColor.FromArgb(overlay.Color.A, overlay.Color.R, overlay.Color.G, overlay.Color.B));
            gfx.DrawString(
                overlay.Text,
                font,
                brush,
                new XPoint(overlay.X * 72.0 / StandardDpi, (overlay.Y + overlay.FontSize) * 72.0 / StandardDpi));
        }
    }

    private static void DrawVectorOverlays(XGraphics gfx, FixedPage page)
    {
        foreach (UIElement child in page.Children)
            DrawVectorOverlays(gfx, child, 0, 0);
    }

    private static void DrawVectorOverlays(XGraphics gfx, UIElement element, double parentX, double parentY)
    {
        if (element.Visibility != Visibility.Visible)
            return;

        var x = parentX + ReadLeft(element);
        var y = parentY + ReadTop(element);

        if (element is FrameworkElement frameworkElement)
        {
            x += frameworkElement.Margin.Left;
            y += frameworkElement.Margin.Top;
        }

        var renderTranslation = ReadSimpleTranslation(element.RenderTransform);
        x += renderTranslation.X;
        y += renderTranslation.Y;

        if (element is VisualHost { Visual: DrawingVisual drawingVisual })
            DrawVectorDrawing(gfx, drawingVisual.Drawing, CreatePageTransform(x, y));

        if (element is Panel panel)
        {
            foreach (UIElement child in panel.Children)
                DrawVectorOverlays(gfx, child, x, y);
        }
        else if (element is Decorator { Child: UIElement decoratorChild })
        {
            DrawVectorOverlays(gfx, decoratorChild, x, y);
        }
        else if (element is ContentControl { Content: UIElement contentChild })
        {
            DrawVectorOverlays(gfx, contentChild, x, y);
        }

        if (element is HeaderedContentControl { Header: UIElement headerChild })
            DrawVectorOverlays(gfx, headerChild, x, y);

        if (element is ItemsControl itemsControlWithElementItems)
        {
            foreach (var item in WpfTextContentExtractor.EnumerateVisibleItemElements(itemsControlWithElementItems))
                DrawVectorOverlays(gfx, item, x, y);
        }
    }

    private static void DrawVectorDrawing(XGraphics gfx, Drawing drawing, Matrix transform)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                var groupTransform = transform;
                if (group.Transform is not null && group.Transform != Transform.Identity)
                    groupTransform.Append(group.Transform.Value);

                foreach (var child in group.Children)
                    DrawVectorDrawing(gfx, child, groupTransform);
                break;
            case GeometryDrawing geometryDrawing:
                DrawVectorGeometry(gfx, geometryDrawing, transform);
                break;
        }
    }

    private static void DrawVectorGeometry(XGraphics gfx, GeometryDrawing drawing, Matrix transform)
    {
        if (drawing.Geometry is null)
            return;

        var brush = TryCreateBrush(drawing.Brush);
        var pen = TryCreatePen(drawing.Pen);
        if (brush is null && pen is null)
            return;

        var geometry = drawing.Geometry.Clone();
        var geometryTransform = transform;
        if (geometry.Transform is not null && geometry.Transform != Transform.Identity)
            geometryTransform.Append(geometry.Transform.Value);
        geometry.Transform = new MatrixTransform(geometryTransform);

        var pathGeometry = geometry.GetFlattenedPathGeometry();
        if (pathGeometry.Figures.Count == 0)
            return;

        var path = new XGraphicsPath(pathGeometry);
        gfx.DrawPath(pen, brush, path);
    }

    private static Matrix CreatePageTransform(double x, double y) =>
        new(72.0 / StandardDpi, 0, 0, 72.0 / StandardDpi, x * 72.0 / StandardDpi, y * 72.0 / StandardDpi);

    private static XSolidBrush? TryCreateBrush(Brush? brush)
    {
        if (brush is not SolidColorBrush solid)
            return null;

        var color = solid.Color;
        return new XSolidBrush(XColor.FromArgb(color.A, color.R, color.G, color.B));
    }

    private static XPen? TryCreatePen(System.Windows.Media.Pen? pen)
    {
        if (pen is null || pen.Thickness <= 0 || pen.Brush is not SolidColorBrush solid)
            return null;

        var color = solid.Color;
        return new XPen(XColor.FromArgb(color.A, color.R, color.G, color.B), pen.Thickness * 72.0 / StandardDpi);
    }

    private static double ReadLeft(UIElement element)
    {
        var left = Canvas.GetLeft(element);
        return double.IsNaN(left) ? 0 : left;
    }

    private static double ReadTop(UIElement element)
    {
        var top = Canvas.GetTop(element);
        return double.IsNaN(top) ? 0 : top;
    }

    private static Vector ReadSimpleTranslation(Transform? transform)
    {
        return TryReadSimpleTranslation(transform, out var translation)
            ? translation
            : default;
    }

    private static bool TryReadSimpleTranslation(Transform? transform, out Vector translation)
    {
        if (transform is null || transform == Transform.Identity)
        {
            translation = default;
            return true;
        }

        switch (transform)
        {
            case TranslateTransform translate:
                translation = new Vector(translate.X, translate.Y);
                return true;
            case MatrixTransform matrixTransform when IsOffsetOnly(matrixTransform.Matrix):
                translation = new Vector(matrixTransform.Matrix.OffsetX, matrixTransform.Matrix.OffsetY);
                return true;
            case TransformGroup group:
                return TryReadSimpleTranslation(group, out translation);
            default:
                translation = default;
                return false;
        }
    }

    private static bool TryReadSimpleTranslation(TransformGroup group, out Vector translation)
    {
        var result = new Vector();
        foreach (var child in group.Children)
        {
            if (!TryReadSimpleTranslation(child, out var childTranslation))
            {
                translation = default;
                return false;
            }

            result += childTranslation;
        }

        translation = result;
        return true;
    }

    private static bool IsOffsetOnly(Matrix matrix) =>
        matrix.M11 == 1 &&
        matrix.M12 == 0 &&
        matrix.M21 == 0 &&
        matrix.M22 == 1;

    private static void AddLinkAnnotations(PdfPage pdfPage, FixedPage fixedPage)
    {
        foreach (var overlay in PdfLinkOverlayExtractor.Extract(fixedPage))
        {
            var uri = NormalizeLinkAnnotationUri(overlay);
            if (uri is null ||
                overlay.Width <= 0 ||
                overlay.Height <= 0)
            {
                continue;
            }

            if (!TryCreateLinkAnnotationRect(pdfPage, overlay, out var rect) || rect is null)
                continue;

            var annotations = pdfPage.Elements.GetArray("/Annots");
            if (annotations is null)
            {
                annotations = new PdfArray(pdfPage.Owner);
                pdfPage.Elements["/Annots"] = annotations;
            }

            var action = new PdfDictionary(pdfPage.Owner);
            action.Elements.SetName("/S", "/URI");
            action.Elements.SetString("/URI", uri);

            var annotation = new PdfDictionary(pdfPage.Owner);
            annotation.Elements.SetName("/Type", "/Annot");
            annotation.Elements.SetName("/Subtype", "/Link");
            annotation.Elements.SetRectangle("/Rect", rect);
            annotation.Elements.SetName("/H", "/I");
            annotation.Elements.SetInteger("/F", 4);
            annotation.Elements["/Border"] = CreateInvisibleAnnotationBorder(pdfPage.Owner);
            annotation.Elements.SetString("/Contents", uri);
            annotation.Elements["/A"] = action;
            annotations.Elements.Add(annotation);
        }
    }

    private static bool TryCreateLinkAnnotationRect(PdfPage pdfPage, PdfLinkOverlay overlay, out PdfRectangle? rect)
    {
        var left = overlay.X * 72.0 / StandardDpi;
        var right = (overlay.X + overlay.Width) * 72.0 / StandardDpi;
        var top = pdfPage.Height.Point - overlay.Y * 72.0 / StandardDpi;
        var bottom = pdfPage.Height.Point - (overlay.Y + overlay.Height) * 72.0 / StandardDpi;

        left = Math.Clamp(left, 0, pdfPage.Width.Point);
        right = Math.Clamp(right, 0, pdfPage.Width.Point);
        bottom = Math.Clamp(bottom, 0, pdfPage.Height.Point);
        top = Math.Clamp(top, 0, pdfPage.Height.Point);

        if (right <= left || top <= bottom)
        {
            rect = default;
            return false;
        }

        rect = new PdfRectangle(new XRect(left, bottom, right - left, top - bottom));
        return true;
    }

    private static PdfArray CreateInvisibleAnnotationBorder(PdfDocument owner)
    {
        var border = new PdfArray(owner);
        border.Elements.Add(new PdfInteger(0));
        border.Elements.Add(new PdfInteger(0));
        border.Elements.Add(new PdfInteger(0));
        return border;
    }

    private static string? NormalizeLinkAnnotationUri(PdfLinkOverlay overlay)
    {
        if (overlay.TargetKind == HyperlinkTargetKind.PlaceInThisDocument)
            return null;

        var target = overlay.Target.Trim();
        if (target.Length == 0)
            return null;

        if (overlay.TargetKind == HyperlinkTargetKind.EmailAddress &&
            !target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return "mailto:" + target;
        }

        if (overlay.TargetKind is HyperlinkTargetKind.ExistingFileOrWebPage or HyperlinkTargetKind.CreateNewDocument &&
            (!HasUriScheme(target) || IsWindowsDrivePath(target)))
        {
            if (IsUncPath(target))
                return "file://" + target.TrimStart('\\', '/').Replace('\\', '/');

            return "file:///" + target.Replace('\\', '/').TrimStart('/');
        }

        return target;
    }

    private static bool HasUriScheme(string target)
    {
        var colonIndex = target.IndexOf(':');
        if (colonIndex <= 0)
            return false;

        for (var i = 0; i < colonIndex; i++)
        {
            var ch = target[i];
            if (i == 0 && !char.IsAsciiLetter(ch))
                return false;
            if (!char.IsAsciiLetterOrDigit(ch) && ch is not '+' and not '-' and not '.')
                return false;
        }

        return true;
    }

    private static bool IsWindowsDrivePath(string target) =>
        target.Length >= 3 &&
        char.IsAsciiLetter(target[0]) &&
        target[1] == ':' &&
        (target[2] == '\\' || target[2] == '/');

    private static bool IsUncPath(string target) =>
        target.StartsWith(@"\\", StringComparison.Ordinal) ||
        target.StartsWith("//", StringComparison.Ordinal);
}
