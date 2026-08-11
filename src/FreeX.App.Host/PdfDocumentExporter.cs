using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.App.Services;
using FreeX.Core.Model;
using Free.Shared.Pdf.Wpf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SharedPdf = Free.Shared.Pdf;

namespace FreeX.App.Host;

internal sealed record PdfBookmark(string Title, int PageIndex);

/// <summary>
/// Renders a paginated FreeX <see cref="FixedDocument"/> to a real PDF.
///
/// <para>
/// The raster image, selectable-text overlays, external-URI link annotations and document Info
/// metadata are emitted through the shared <see cref="WpfRasterPdfWriter"/> (PDFsharp) so FreeX and
/// FreeW share one rasterized-page → PDF emitter rather than each carrying its own PDFsharp plumbing.
/// FreeX layers its spreadsheet-specific extras on top through the writer's hooks: vector overlays
/// (gridlines/borders/shapes and gradient fills) via the per-page draw hook, and bookmarks/outlines,
/// viewer preferences, the catalog <c>/Lang</c>, the <c>/DisplayDocTitle</c> preference and internal
/// cross-page <c>/Dest</c> link destinations via the document-configuration hook.
/// </para>
/// </summary>
internal static class PdfDocumentExporter
{
    private const double StandardDpi = 96.0;
    private const double MinimumSizeDpi = 72.0;

    // WPF lays visuals out in device-independent pixels (1/96 inch); PDF user space is points (1/72 inch).
    private const double DipToPoint = 72.0 / StandardDpi;

    private sealed record PdfInternalDestination(PdfPage Page, XPoint Point);

    // A page selected for export: the laid-out source page plus the internal (place-in-this-document)
    // link overlays that resolve to /Dest annotations once every PDF page exists.
    private sealed record ExportPage(FixedPage FixedPage, IReadOnlyList<PdfLinkOverlay> InternalLinks);

    public static void Save(
        FixedDocument document,
        string path,
        SharedPdf.PdfDocumentProperties? properties = null,
        string pdfLanguage = ExportPlanner.DefaultPdfLanguage)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        SavePages(document, path, properties, firstPageIndex: 0, lastPageIndexInclusive: document.Pages.Count - 1, pdfLanguage: pdfLanguage);
    }

    public static void Save(
        FixedDocument document,
        string path,
        SharedPdf.PdfDocumentProperties? properties,
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

        if (!ExportPlanner.TryValidatePageRange(pageRange, document.Pages.Count, out var pageRangeError, WpfExportPlannerTextResolver.Instance))
            throw new InvalidOperationException(pageRangeError);

        var firstPageIndex = Math.Max(0, (pageRange?.FromPage ?? 1) - 1);
        var lastPageIndexInclusive = Math.Min(document.Pages.Count - 1, (pageRange?.ToPage ?? document.Pages.Count) - 1);
        SavePages(document, path, properties, firstPageIndex, lastPageIndexInclusive, ResolveRasterDpi(quality), bookmarks, initialView, openMode, includeSelectableText, pdfLanguage);
    }

    /// <summary>
    /// Renders <paramref name="document"/> into PDF bytes without writing any file.  The caller
    /// may then flush the bytes to disk on a background thread via
    /// <see cref="ExportAtomicWriter.WriteAllBytes"/>.  This overload must be called on the
    /// UI / STA thread because it accesses WPF visual objects.
    /// </summary>
    public static byte[] RenderToBytes(
        FixedDocument document,
        SharedPdf.PdfDocumentProperties? properties,
        ExportPageRange? pageRange,
        ExportQuality quality = ExportQuality.Standard,
        IReadOnlyList<PdfBookmark>? bookmarks = null,
        PdfInitialView initialView = PdfInitialView.SinglePage,
        PdfOpenMode openMode = PdfOpenMode.Normal,
        bool includeSelectableText = false,
        string pdfLanguage = ExportPlanner.DefaultPdfLanguage)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!ExportPlanner.TryValidatePageRange(pageRange, document.Pages.Count, out var pageRangeError, WpfExportPlannerTextResolver.Instance))
            throw new InvalidOperationException(pageRangeError);

        var firstPageIndex = Math.Max(0, (pageRange?.FromPage ?? 1) - 1);
        var lastPageIndexInclusive = Math.Min(document.Pages.Count - 1, (pageRange?.ToPage ?? document.Pages.Count) - 1);
        using var stream = new MemoryStream();
        BuildPages(document, stream, properties, firstPageIndex, lastPageIndexInclusive, ResolveRasterDpi(quality), bookmarks, initialView, openMode, includeSelectableText, pdfLanguage);
        return stream.ToArray();
    }

    internal static double ResolveRasterDpi(ExportQuality quality) =>
        quality == ExportQuality.MinimumSize
            ? MinimumSizeDpi
            : StandardDpi;

    private static void SavePages(
        FixedDocument document,
        string path,
        SharedPdf.PdfDocumentProperties? properties,
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

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        BuildPages(document, stream, properties, firstPageIndex, lastPageIndexInclusive, dpi, bookmarks, initialView, openMode, includeSelectableText, pdfLanguage);
    }

    private static void BuildPages(
        FixedDocument document,
        Stream outputStream,
        SharedPdf.PdfDocumentProperties? properties,
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

        var exportPages = new List<ExportPage>();
        var rasterPages = new List<SharedPdf.PdfRasterPage>();
        for (int i = firstPageIndex; i <= lastPageIndexInclusive; i++)
        {
            var fixedPage = GetFixedPage(document.Pages[i]);
            var pageSize = GetPageSize(document, fixedPage);
            fixedPage.Measure(pageSize);
            fixedPage.Arrange(new Rect(pageSize));
            fixedPage.UpdateLayout();

            var imageBytes = EncodePng(RenderPage(fixedPage, pageSize, dpi));
            var textOverlays = includeSelectableText ? BuildTextOverlays(fixedPage) : null;
            var (uriLinks, internalLinks) = BuildLinkOverlays(fixedPage);

            rasterPages.Add(new SharedPdf.PdfRasterPage(
                pageSize.Width * DipToPoint,
                pageSize.Height * DipToPoint,
                imageBytes,
                textOverlays,
                uriLinks));
            exportPages.Add(new ExportPage(fixedPage, internalLinks));
        }

        var rasterDocument = new SharedPdf.PdfRasterDocument(rasterPages, WithDefaultCreator(properties));
        var normalizedTitle = ExportDocumentPropertiesPlanner.Normalize(properties?.Title);

        WpfRasterPdfWriter.Write(
            rasterDocument,
            outputStream,
            drawPageContent: (gfx, _, index) => DrawVectorOverlays(gfx, exportPages[index].FixedPage),
            configureDocument: pdf => ConfigureDocument(
                pdf, exportPages, bookmarks, firstPageIndex, lastPageIndexInclusive,
                initialView, openMode, normalizedTitle, pdfLanguage),
            uncompressedContent: includeSelectableText);
    }

    internal static SharedPdf.PdfDocumentProperties? CreateProperties(Workbook workbook, ExportOptions options)
    {
        if (ExportDocumentPropertiesPlanner.FromWorkbook(workbook, options) is not { } properties)
            return null;

        return new SharedPdf.PdfDocumentProperties(
            properties.Title,
            properties.Creator,
            properties.Subject,
            properties.Keywords,
            ExportDocumentPropertiesPlanner.DefaultCreator);
    }

    private static SharedPdf.PdfDocumentProperties WithDefaultCreator(
        SharedPdf.PdfDocumentProperties? properties) =>
        (properties ?? new SharedPdf.PdfDocumentProperties()) with
        {
            Creator = ExportDocumentPropertiesPlanner.DefaultCreator,
        };

    private static void ConfigureDocument(
        PdfDocument pdf,
        IReadOnlyList<ExportPage> exportPages,
        IReadOnlyList<PdfBookmark>? bookmarks,
        int firstPageIndex,
        int lastPageIndexInclusive,
        PdfInitialView initialView,
        PdfOpenMode openMode,
        string? normalizedTitle,
        string pdfLanguage)
    {
        ApplyDefaultCatalogMetadata(pdf, pdfLanguage);
        ApplyDefaultViewerPreferences(pdf, initialView);
        if (normalizedTitle is not null)
            SetDisplayDocumentTitlePreference(pdf);

        AddInternalLinkAnnotations(pdf, exportPages);

        var hasBookmarks = AddBookmarks(pdf, bookmarks, firstPageIndex, lastPageIndexInclusive);
        ApplyOpenMode(pdf, openMode, hasBookmarks);
    }

    private static IReadOnlyList<SharedPdf.PdfTextOverlay>? BuildTextOverlays(FixedPage page)
    {
        var extracted = PdfTextOverlayExtractor.Extract(page);
        if (extracted.Count == 0)
            return null;

        var overlays = new List<SharedPdf.PdfTextOverlay>(extracted.Count);
        foreach (var overlay in extracted)
        {
            overlays.Add(new SharedPdf.PdfTextOverlay(
                X: overlay.X * DipToPoint,
                Y: overlay.Y * DipToPoint,
                FontSize: overlay.FontSize * DipToPoint,
                FontFamily: overlay.FontFamily,
                Bold: overlay.Bold,
                Italic: overlay.Italic,
                Color: new SharedPdf.PdfColor(overlay.Color.R, overlay.Color.G, overlay.Color.B),
                RotationDegrees: overlay.RotationDegrees,
                Text: overlay.Text));
        }

        return overlays;
    }

    // Splits the page's hyperlink overlays into the external-URI links routed through the shared writer
    // (converted to PDF points) and the internal place-in-this-document links FreeX resolves locally to
    // /Dest annotations once every PDF page exists.
    private static (IReadOnlyList<SharedPdf.PdfLinkOverlay>? Uri, IReadOnlyList<PdfLinkOverlay> Internal) BuildLinkOverlays(FixedPage page)
    {
        var extracted = PdfLinkOverlayExtractor.Extract(page);
        if (extracted.Count == 0)
            return (null, []);

        List<SharedPdf.PdfLinkOverlay>? uriLinks = null;
        List<PdfLinkOverlay>? internalLinks = null;
        foreach (var overlay in extracted)
        {
            if (overlay.Width <= 0 || overlay.Height <= 0)
                continue;

            if (overlay.TargetKind == HyperlinkTargetKind.PlaceInThisDocument)
            {
                (internalLinks ??= []).Add(overlay);
                continue;
            }

            if (NormalizeLinkAnnotationUri(overlay) is not { } uri)
                continue;

            (uriLinks ??= []).Add(new SharedPdf.PdfLinkOverlay(
                X: overlay.X * DipToPoint,
                Y: overlay.Y * DipToPoint,
                Width: overlay.Width * DipToPoint,
                Height: overlay.Height * DipToPoint,
                Uri: uri,
                Tooltip: null));
        }

        return (uriLinks, internalLinks ?? []);
    }

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
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
        new(DipToPoint, 0, 0, DipToPoint, x * DipToPoint, y * DipToPoint);

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
            : DipToPoint;
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

    private static IReadOnlyDictionary<CellAddress, PdfInternalDestination> BuildInternalDestinationLookup(
        PdfDocument pdf,
        IReadOnlyList<ExportPage> exportPages)
    {
        var result = new Dictionary<CellAddress, PdfInternalDestination>();
        for (int i = 0; i < exportPages.Count; i++)
        {
            var pdfPage = pdf.Pages[i];
            foreach (var overlay in PdfCellDestinationOverlayExtractor.Extract(exportPages[i].FixedPage))
            {
                if (result.ContainsKey(overlay.Address) ||
                    overlay.Width <= 0 ||
                    overlay.Height <= 0 ||
                    !TryCreateInternalDestinationPoint(pdfPage, overlay, out var point))
                {
                    continue;
                }

                result[overlay.Address] = new PdfInternalDestination(pdfPage, point);
            }
        }

        return result;
    }

    private static bool TryCreateInternalDestinationPoint(
        PdfPage pdfPage,
        PdfCellDestinationOverlay overlay,
        out XPoint point)
    {
        var left = overlay.X * DipToPoint;
        var top = pdfPage.Height.Point - overlay.Y * DipToPoint;

        left = Math.Clamp(left, 0, pdfPage.Width.Point);
        top = Math.Clamp(top, 0, pdfPage.Height.Point);

        if (!IsFinite(left) || !IsFinite(top))
        {
            point = default;
            return false;
        }

        point = new XPoint(left, top);
        return true;
    }

    // Internal (place-in-this-document) hyperlinks become PDF /Dest annotations once every page exists,
    // so they are stamped here rather than through the shared writer's external-URI overlay path.
    private static void AddInternalLinkAnnotations(PdfDocument pdf, IReadOnlyList<ExportPage> exportPages)
    {
        if (exportPages.All(page => page.InternalLinks.Count == 0))
            return;

        var internalDestinations = BuildInternalDestinationLookup(pdf, exportPages);
        for (int i = 0; i < exportPages.Count; i++)
        {
            var pdfPage = pdf.Pages[i];
            foreach (var overlay in exportPages[i].InternalLinks)
            {
                if (overlay.TargetAddress is not { } targetAddress ||
                    !internalDestinations.TryGetValue(targetAddress, out var destination))
                {
                    continue;
                }

                if (!TryCreateLinkAnnotationRect(pdfPage, overlay, out var rect) || rect is null)
                    continue;

                AddInternalLinkAnnotation(pdfPage, overlay, rect, destination);
            }
        }
    }

    private static void AddInternalLinkAnnotation(
        PdfPage pdfPage,
        PdfLinkOverlay overlay,
        PdfRectangle rect,
        PdfInternalDestination destination)
    {
        var destinationArray = new PdfArray(pdfPage.Owner);
        destinationArray.Elements.Add(destination.Page.ReferenceNotNull);
        destinationArray.Elements.Add(new PdfName("/XYZ"));
        destinationArray.Elements.Add(new PdfReal(destination.Point.X));
        destinationArray.Elements.Add(new PdfReal(destination.Point.Y));
        destinationArray.Elements.Add(PdfNull.Value);

        var annotation = CreateBaseLinkAnnotation(pdfPage, rect, overlay.Target);
        annotation.Elements["/Dest"] = destinationArray;
        GetOrCreateAnnotations(pdfPage).Elements.Add(annotation);
    }

    private static PdfDictionary CreateBaseLinkAnnotation(PdfPage pdfPage, PdfRectangle rect, string contents)
    {
        var annotation = new PdfDictionary(pdfPage.Owner);
        annotation.Elements.SetName("/Type", "/Annot");
        annotation.Elements.SetName("/Subtype", "/Link");
        annotation.Elements.SetRectangle("/Rect", rect);
        annotation.Elements.SetName("/H", "/I");
        annotation.Elements.SetInteger("/F", 4);
        annotation.Elements["/Border"] = CreateInvisibleAnnotationBorder(pdfPage.Owner);
        annotation.Elements.SetString("/Contents", contents);
        return annotation;
    }

    private static PdfArray GetOrCreateAnnotations(PdfPage pdfPage)
    {
        var annotations = pdfPage.Elements.GetArray("/Annots");
        if (annotations is not null)
            return annotations;

        annotations = new PdfArray(pdfPage.Owner);
        pdfPage.Elements["/Annots"] = annotations;
        return annotations;
    }

    private static bool TryCreateLinkAnnotationRect(PdfPage pdfPage, PdfLinkOverlay overlay, out PdfRectangle? rect)
    {
        var left = overlay.X * DipToPoint;
        var right = (overlay.X + overlay.Width) * DipToPoint;
        var top = pdfPage.Height.Point - overlay.Y * DipToPoint;
        var bottom = pdfPage.Height.Point - (overlay.Y + overlay.Height) * DipToPoint;

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
