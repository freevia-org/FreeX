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
        var pageTransform = CreatePageTransform(0, 0);
        foreach (UIElement child in page.Children)
            DrawVectorOverlays(gfx, child, pageTransform);
    }

    private static void DrawVectorOverlays(XGraphics gfx, UIElement element, Matrix parentTransform)
    {
        if (element.Visibility != Visibility.Visible)
            return;

        var elementTransform = CreateElementTransform(element, parentTransform);

        if (element is VisualHost { Visual: DrawingVisual drawingVisual })
            DrawVectorDrawing(gfx, drawingVisual.Drawing, elementTransform, opacity: 1.0);

        if (element is Panel panel)
        {
            foreach (UIElement child in panel.Children)
                DrawVectorOverlays(gfx, child, elementTransform);
        }
        else if (element is Decorator { Child: UIElement decoratorChild })
        {
            DrawVectorOverlays(gfx, decoratorChild, elementTransform);
        }
        else if (element is ContentControl { Content: UIElement contentChild })
        {
            DrawVectorOverlays(gfx, contentChild, elementTransform);
        }

        if (element is HeaderedContentControl { Header: UIElement headerChild })
            DrawVectorOverlays(gfx, headerChild, elementTransform);

        if (element is ItemsControl itemsControlWithElementItems)
        {
            foreach (var item in WpfTextContentExtractor.EnumerateVisibleItemElements(itemsControlWithElementItems))
                DrawVectorOverlays(gfx, item, elementTransform);
        }
    }

    private static void DrawVectorDrawing(XGraphics gfx, Drawing drawing, Matrix transform, double opacity)
    {
        if (opacity <= 0)
            return;

        switch (drawing)
        {
            case DrawingGroup group:
                var groupTransform = CreateDrawingTransform(transform, group.Transform);
                var groupOpacity = opacity * CoerceOpacity(group.Opacity);
                foreach (var child in group.Children)
                    DrawVectorDrawing(gfx, child, groupTransform, groupOpacity);
                break;
            case GeometryDrawing geometryDrawing:
                DrawVectorGeometry(gfx, geometryDrawing, transform, opacity);
                break;
        }
    }

    private static void DrawVectorGeometry(XGraphics gfx, GeometryDrawing drawing, Matrix transform, double opacity)
    {
        if (drawing.Geometry is null)
            return;

        var geometryTransform = CreateDrawingTransform(transform, drawing.Geometry.Transform);
        var brush = TryCreateBrush(drawing.Brush, drawing.Geometry.Bounds, geometryTransform, opacity);
        var pen = TryCreatePen(drawing.Pen, geometryTransform, opacity);
        if (brush is null && pen is null)
            return;

        var geometry = drawing.Geometry.Clone();
        geometry.Transform = new MatrixTransform(geometryTransform);

        var pathGeometry = geometry.GetFlattenedPathGeometry();
        if (pathGeometry.Figures.Count == 0)
            return;

        var path = new XGraphicsPath(pathGeometry);
        gfx.DrawPath(pen, brush, path);
    }

    private static Matrix CreatePageTransform(double x, double y) =>
        new(72.0 / StandardDpi, 0, 0, 72.0 / StandardDpi, x * 72.0 / StandardDpi, y * 72.0 / StandardDpi);

    private static Matrix CreateElementTransform(UIElement element, Matrix parentTransform)
    {
        var elementTransform = Matrix.Identity;
        if (TryGetFiniteMatrix(element.RenderTransform, out var renderTransform) && !renderTransform.IsIdentity)
        {
            var origin = GetRenderTransformOrigin(element);
            if (!IsZeroPoint(origin))
                AppendTranslation(ref elementTransform, -origin.X, -origin.Y);

            elementTransform.Append(renderTransform);

            if (!IsZeroPoint(origin))
                AppendTranslation(ref elementTransform, origin.X, origin.Y);
        }

        var x = ReadLeft(element);
        var y = ReadTop(element);
        if (element is FrameworkElement frameworkElement)
        {
            x += frameworkElement.Margin.Left;
            y += frameworkElement.Margin.Top;
        }

        AppendTranslation(ref elementTransform, x, y);
        elementTransform.Append(parentTransform);
        return elementTransform;
    }

    private static Matrix CreateDrawingTransform(Matrix parentTransform, Transform? drawingTransform)
    {
        if (!TryGetFiniteMatrix(drawingTransform, out var localTransform) || localTransform.IsIdentity)
            return parentTransform;

        localTransform.Append(parentTransform);
        return localTransform;
    }

    private static void AppendTranslation(ref Matrix matrix, double x, double y) =>
        matrix.Append(new Matrix(1, 0, 0, 1, x, y));

    private static Point GetRenderTransformOrigin(UIElement element)
    {
        if (element is not FrameworkElement frameworkElement)
            return default;

        var origin = frameworkElement.RenderTransformOrigin;
        if (IsZero(origin.X) && IsZero(origin.Y))
            return default;

        var width = ResolveFiniteLength(frameworkElement.ActualWidth, frameworkElement.RenderSize.Width);
        var height = ResolveFiniteLength(frameworkElement.ActualHeight, frameworkElement.RenderSize.Height);
        if (width <= 0 || height <= 0)
            return default;

        return new Point(origin.X * width, origin.Y * height);
    }

    private static double ResolveFiniteLength(double preferred, double fallback)
    {
        if (IsFinite(preferred) && preferred > 0)
            return preferred;

        return IsFinite(fallback) && fallback > 0 ? fallback : 0;
    }

    private static XBrush? TryCreateBrush(Brush? brush, Rect geometryBounds, Matrix transform, double opacity)
    {
        return brush switch
        {
            SolidColorBrush solid => new XSolidBrush(ToXColor(solid.Color, opacity * solid.Opacity)),
            LinearGradientBrush linear => TryCreateLinearGradientBrush(linear, geometryBounds, transform, opacity),
            _ => null
        };
    }

    private static XBrush? TryCreateLinearGradientBrush(
        LinearGradientBrush brush,
        Rect geometryBounds,
        Matrix transform,
        double opacity)
    {
        if (brush.GradientStops.Count == 0 ||
            brush.SpreadMethod != GradientSpreadMethod.Pad ||
            HasNonIdentityTransform(brush.Transform) ||
            HasNonIdentityTransform(brush.RelativeTransform))
        {
            return null;
        }

        var stops = brush.GradientStops
            .OrderBy(stop => stop.Offset)
            .ToArray();
        if (stops.Length == 1)
            return new XSolidBrush(ToXColor(stops[0].Color, opacity * brush.Opacity));

        if (brush.MappingMode == BrushMappingMode.RelativeToBoundingBox && !IsUsableRect(geometryBounds))
            return null;

        var start = ResolveGradientPoint(brush.StartPoint, geometryBounds, brush.MappingMode);
        var end = ResolveGradientPoint(brush.EndPoint, geometryBounds, brush.MappingMode);
        if (!IsFinite(start) || !IsFinite(end) || AreClose(start, end))
            return null;

        start = transform.Transform(start);
        end = transform.Transform(end);
        if (!IsFinite(start) || !IsFinite(end) || AreClose(start, end))
            return null;

        return new XLinearGradientBrush(
            new XPoint(start.X, start.Y),
            new XPoint(end.X, end.Y),
            ToXColor(stops[0].Color, opacity * brush.Opacity),
            ToXColor(stops[^1].Color, opacity * brush.Opacity));
    }

    private static Point ResolveGradientPoint(Point point, Rect geometryBounds, BrushMappingMode mappingMode)
    {
        if (mappingMode == BrushMappingMode.Absolute)
            return point;

        return new Point(
            geometryBounds.X + point.X * geometryBounds.Width,
            geometryBounds.Y + point.Y * geometryBounds.Height);
    }

    private static bool HasNonIdentityTransform(Transform transform) =>
        transform != Transform.Identity &&
        (!TryGetFiniteMatrix(transform, out var matrix) || !matrix.IsIdentity);

    private static XPen? TryCreatePen(System.Windows.Media.Pen? pen, Matrix transform, double opacity)
    {
        if (pen is null || pen.Thickness <= 0 || pen.Brush is not SolidColorBrush solid)
            return null;

        var width = pen.Thickness * EstimateStrokeScale(transform);
        if (!IsFinite(width) || width <= 0)
            return null;

        return new XPen(ToXColor(solid.Color, opacity * solid.Opacity), width);
    }

    private static double EstimateStrokeScale(Matrix transform)
    {
        var scaleX = Math.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12);
        var scaleY = Math.Sqrt(transform.M21 * transform.M21 + transform.M22 * transform.M22);
        var scale = (scaleX + scaleY) / 2.0;

        return IsFinite(scale) && scale > 0
            ? scale
            : 72.0 / StandardDpi;
    }

    private static XColor ToXColor(Color color, double opacity)
    {
        var alpha = (int)Math.Round(color.A * CoerceOpacity(opacity), MidpointRounding.AwayFromZero);
        return XColor.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static double CoerceOpacity(double opacity)
    {
        if (double.IsNaN(opacity))
            return 1.0;

        return Math.Clamp(opacity, 0.0, 1.0);
    }

    private static bool TryGetFiniteMatrix(Transform? transform, out Matrix matrix)
    {
        if (transform is null || transform == Transform.Identity)
        {
            matrix = Matrix.Identity;
            return true;
        }

        matrix = transform.Value;
        return IsFinite(matrix);
    }

    private static bool IsFinite(Matrix matrix) =>
        IsFinite(matrix.M11) &&
        IsFinite(matrix.M12) &&
        IsFinite(matrix.M21) &&
        IsFinite(matrix.M22) &&
        IsFinite(matrix.OffsetX) &&
        IsFinite(matrix.OffsetY);

    private static bool IsFinite(Point point) =>
        IsFinite(point.X) &&
        IsFinite(point.Y);

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool IsUsableRect(Rect rect) =>
        IsFinite(rect.X) &&
        IsFinite(rect.Y) &&
        IsFinite(rect.Width) &&
        IsFinite(rect.Height) &&
        rect.Width > 0 &&
        rect.Height > 0;

    private static bool AreClose(Point first, Point second) =>
        IsZero(first.X - second.X) &&
        IsZero(first.Y - second.Y);

    private static bool IsZeroPoint(Point point) =>
        IsZero(point.X) &&
        IsZero(point.Y);

    private static bool IsZero(double value) =>
        Math.Abs(value) < 0.000001;

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
