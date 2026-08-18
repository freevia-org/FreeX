using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Shell;
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
    public PrintPreviewWindow(DocumentView editor, string? displayName = null)
    {
        var source = PrintLayout.BuildPaginatedSource(editor);
        source.DocumentPaginator.ComputePageCount();
        var preview = new FreeWPrintPreviewSession(
            displayName,
            editor.Model.Page,
            BackstageDirectPrintCapability.NativeDialogAvailable(),
            canCreatePdf: false,
            canDirectPrint: true);
        var state = preview.SetPageCount(source.DocumentPaginator.PageCount);

        Title = state.Title;
        Width = 900;
        Height = 760;
        Background = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
        Owner = Window.GetWindow(editor);

        var viewer = new DocumentViewer
        {
            Document = source,
            ToolTip = state.PageCountText,
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
    private const double FootnoteContinuationTrailingReserveDip = 15.0;

    /// <summary>
    /// Produces a fresh <see cref="FlowDocument"/> whose page size and margins match the model's
    /// <see cref="PageSettings"/>, carrying a display-only clone of the editor's content. The clone is
    /// taken via XAML round-tripping over the editor's FlowDocument so this path never reaches into
    /// the model&lt;-&gt;FlowDocument mapping owned by <see cref="DocumentView"/>.
    /// </summary>
    public static FlowDocument BuildPaginatedDocument(DocumentView editor) =>
        BuildPaginatedDocument(editor, applyFragmentedFootnoteFlow: false, out _);

    /// <summary>
    /// Width, in DIP, reserved on the right of the printed/exported page for the Review &gt; Show Markup
    /// &gt; Balloons strip when <see cref="DocumentView.ShowMarkupBalloons"/> is on and the document has
    /// at least one comment or shown tracked-change revision. Word's own behaviour: the printed page
    /// widens to include the same markup area shown on screen, rather than dropping it.
    /// </summary>
    internal const double BalloonStripWidthDip = 200.0;

    /// <summary>
    /// The balloon sources (comments + shown tracked-change revisions, in document order) that
    /// Print/Print Preview/PDF/XPS export should draw in a right-margin strip, or empty when balloons
    /// mode is off or the document has nothing to show. Mirrors what <c>BalloonOverlay</c> (WPF) /
    /// <c>ReviewBalloonsPane</c> (Avalonia) already render on screen for Review &gt; Show Markup &gt;
    /// Balloons — this is the family fix for that same on-screen content never reaching Print/PDF/XPS.
    /// </summary>
    internal static IReadOnlyList<ReviewBalloonSource> ResolvePrintBalloonSources(DocumentView editor) =>
        editor.ShowMarkupBalloons
            ? ReviewBalloonLayoutPlanner.BuildSources(editor.Model, editor.CurrentReviewDisplayPolicy)
            : Array.Empty<ReviewBalloonSource>();

    private static FlowDocument BuildPaginatedDocument(
        DocumentView editor,
        bool applyFragmentedFootnoteFlow,
        out FootnoteContinuationFlowComposition? footnoteComposition,
        IReadOnlyList<ReviewBalloonSource>? balloonSources = null)
    {
        var page = editor.Model.Page;
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(page);
        var (left, top, right, bottom) = PageLayout.MarginsDip(page);
        // Widen the page (not the body content width) to add the balloon strip purely as extra space
        // on the right, matching Word's own "the page gets wider" behaviour instead of squeezing body
        // text to make room.
        if (balloonSources is { Count: > 0 })
            pageWidth += BalloonStripWidthDip;

        var flow = new FlowDocument
        {
            PageWidth = pageWidth,
            PageHeight = pageHeight,
            PagePadding = new Thickness(left, top, right, bottom),
            FontFamily = editor.Document.FontFamily,
            FontSize = editor.Document.FontSize
        };

        // Mirror the editor's multi-column layout so preview/print match the on-screen rendering.
        DocumentView.ApplyColumnLayout(flow, page, useNativeColumnRule: false);

        var cloneSource = editor.Document;
        DocumentView? inlineBreakRenderSource = null;
        if (DocumentView.HasRendererInlineFlowBreakFragments(editor.Model))
        {
            inlineBreakRenderSource = new DocumentView
            {
                RenderPageBreakMarkers = false,
                RenderInlineFlowBreakFragmentsForPagination = true,
                DisplayForReview = editor.DisplayForReview,
                ShowMarkupInsertionsAndDeletions = editor.ShowMarkupInsertionsAndDeletions,
                ShowMarkupComments = editor.ShowMarkupComments,
                ShowMarkupFormatting = editor.ShowMarkupFormatting
            };
            inlineBreakRenderSource.LoadModel(editor.Model);
            cloneSource = inlineBreakRenderSource.Document;
        }

        foreach (var block in CloneBlocks(cloneSource))
            flow.Blocks.Add(block);

        // XAML cloning strips the private paragraph tags that carry model section breaks. Restore
        // page-type break ownership here so every consumer of this print/preview document sees the
        // same section boundaries, including homogeneous sections that do not need the section-aware
        // geometry/header-footer paginator.
        PaginationEngine.ApplySectionBreakFlags(editor, flow);

        // Word paginates long notes as page-local fragments in the same physical body flow. Keep
        // ordinary notes on the established reserve path and use fragment owners only for the
        // narrow plain-flow overflow signature proven by the fidelity compositor.
        var (contentWidthDip, contentHeightDip) = PageLayout.ContentAreaDip(page);
        if (!applyFragmentedFootnoteFlow
            || !FootnoteContinuationFlowComposer.TryApply(
                flow,
                editor.Model,
                contentWidthDip,
                contentHeightDip,
                FootnoteContinuationTrailingReserveDip,
                out var composition))
        {
            footnoteComposition = null;
            ApplyFootnoteBodyReserve(flow, editor.Model);
        }
        else
        {
            footnoteComposition = composition;
        }

        return flow;
    }

    private static void ApplyFootnoteBodyReserve(FlowDocument flow, TextDocument document)
    {
        if (document.Footnotes.Count == 0)
            return;

        var (_, contentWidthDip) = PageLayout.ContentAreaDip(document.Page);

        // Measure per-page footnote ownership (which footnotes actually land on which page) with a
        // reserve-free provisional pass over the same blocks, then reserve only the tallest single
        // page's footnote region — not the combined height of every footnote in the whole document,
        // which shrinks every page's usable body area far more than Word does whenever a document's
        // footnotes are spread across more than one page. A separate FlowDocument carries the blocks
        // for the provisional pass and hands them straight back afterward, because a WPF element may
        // belong to only one FlowDocument's visual tree at a time.
        var blocks = flow.Blocks.ToList();
        double reserveDip;
        if (blocks.Count == 0)
        {
            reserveDip = 0;
        }
        else
        {
            var provisionalFlow = new FlowDocument
            {
                PageWidth = flow.PageWidth,
                PageHeight = flow.PageHeight,
                PagePadding = flow.PagePadding,
                FontFamily = flow.FontFamily,
                FontSize = flow.FontSize
            };
            flow.Blocks.Clear();
            foreach (var block in blocks)
                provisionalFlow.Blocks.Add(block);

            try
            {
                // Cap the reserve against the page's content height: the estimate grows with the
                // footnote text, so long enough footnotes would reserve the whole body and leave the
                // WPF paginator a non-positive page box, which it rejects — failing print and print
                // preview outright. ComputeMaxPerPageFootnoteReserveDip applies the same clamp.
                reserveDip = PaginationEngine.ComputeMaxPerPageFootnoteReserveDip(
                    provisionalFlow, document.Blocks, document, document.Page, contentWidthDip);
            }
            finally
            {
                provisionalFlow.Blocks.Clear();
                foreach (var block in blocks)
                    flow.Blocks.Add(block);
            }
        }

        if (reserveDip <= 0)
            return;

        flow.PagePadding = new Thickness(
            flow.PagePadding.Left,
            flow.PagePadding.Top,
            flow.PagePadding.Right,
            flow.PagePadding.Bottom + reserveDip);
    }

    /// <summary>
    /// Builds a paginator for the editor content at the model's page geometry, wrapped so the document
    /// header and footer (with a live page number) are drawn into each page's margin areas. Used by
    /// both the preview window and the print path so the two stay in sync.
    /// </summary>
    public static DocumentPaginator BuildPaginator(DocumentView editor)
    {
        if (editor.Model.Sections.Count > 1
            && (NeedsSectionAwareRendering(editor.Model) || HasParitySectionStarts(editor.Model)))
            return SectionAwareDocumentPaginator.Build(editor);

        var page = editor.Model.Page;
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(page);
        var balloonSources = ResolvePrintBalloonSources(editor);
        if (balloonSources.Count > 0)
            pageWidth += BalloonStripWidthDip;

        var flow = BuildPaginatedDocument(editor, applyFragmentedFootnoteFlow: true, out var footnoteComposition, balloonSources);
        var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        paginator.PageSize = new Size(pageWidth, pageHeight);

        IReadOnlyDictionary<int, DocumentFootnoteContinuationPagePlan>? fragmentedFootnotePages = null;
        if (footnoteComposition is not null)
        {
            var ownership = FootnoteContinuationFlowComposer.BuildPageOwnership(paginator, footnoteComposition);
            fragmentedFootnotePages = FootnoteContinuationFlowComposer.BuildPageMap(
                ownership,
                footnoteComposition.ContinuationById);
        }

        // The line height used to estimate text lines for margin line numbering. The editor's
        // FlowDocument FontSize is already in DIP; WPF lays a line out at ~1.33x the font size
        // (LineHeight defaults to FontSize * 4/3).
        var lineHeightDip = editor.Document.FontSize * (4.0 / 3.0);
        return new HeaderFooterPaginator(
            paginator,
            editor.Model,
            page,
            lineHeightDip,
            fragmentedFootnotePages: fragmentedFootnotePages,
            balloonSources: balloonSources);
    }

    private static bool NeedsSectionAwareRendering(TextDocument document) =>
        document.Sections.Any(section =>
            !section.HeadersFooters.IsEmpty
            || PageGeometryDiffers(section.Page, document.Page)
            || LineNumberingDiffers(section.Page, document.Page));

    private static bool HasParitySectionStarts(TextDocument document) =>
        document.Blocks.OfType<FreeW.Core.Model.Paragraph>().Any(paragraph =>
            paragraph.SectionBreak?.BreakKind is SectionBreakKind.EvenPage or SectionBreakKind.OddPage);

    private static bool LineNumberingDiffers(PageSettings left, PageSettings right) =>
        left.LineNumberMode != right.LineNumberMode
        || left.LineNumberStartAt != right.LineNumberStartAt
        || left.LineNumberCountBy != right.LineNumberCountBy;

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
        // The editor stamps non-public Tag payloads (ParagraphTag, RunMarkers, Footnote/EndnoteMarker,
        // HyperlinkInfo, TableCellTag, shape/image/SmartArt models) on its FlowDocument elements so they
        // survive an edit/commit cycle. XamlWriter.Save cannot serialize a non-public type and throws —
        // which crashed Print and Print Preview on essentially any real document (every styled paragraph
        // carries a ParagraphTag). The Tags are metadata only, irrelevant to the printed rendering, so
        // clear them on the source for the duration of the serialization and restore them immediately
        // after. This runs synchronously on the UI thread, so the live editor is left exactly as it was.
        //
        // Decoded bitmaps need the same treatment for the same reason. A picture decoded by
        // DocumentView.DecodeRaster is a BitmapFrameDecode — non-public, so XamlWriter.Save throws —
        // and the fallback placeholder from BuildImagePlaceholder is a RenderTargetBitmap, which has no
        // parameterless constructor, so XamlReader.Load throws. Either one crashed Print, Print Preview,
        // Export to PDF and Export to XPS on any document containing a picture. Detach the sources for
        // the serialization round trip, then hand the originals to the clone: an ImageSource is
        // shareable between elements on one thread, so the printed pages keep the real image content.
        var saved = new List<(DependencyObject Node, object Tag)>();
        var images = new List<Image>();
        var savedSources = new List<ImageSource?>();
        if (element is DependencyObject root)
        {
            StripTags(root, saved);
            CollectImages(root, images);
            foreach (var image in images)
            {
                savedSources.Add(image.Source);
                image.Source = null;
            }
        }

        try
        {
            var xaml = XamlWriter.Save(element);
            using var reader = new StringReader(xaml);
            using var xmlReader = System.Xml.XmlReader.Create(reader);
            var clone = XamlReader.Load(xmlReader);
            if (clone is DependencyObject cloneRoot)
                RestoreImageSources(cloneRoot, savedSources);
            return clone;
        }
        finally
        {
            for (var i = 0; i < images.Count; i++)
                images[i].Source = savedSources[i];
            foreach (var (node, tag) in saved)
                SetTag(node, tag);
        }
    }

    /// <summary>
    /// Collects every <see cref="Image"/> in the logical tree, in the same deterministic document order
    /// that <see cref="XamlWriter"/> emits them, so source and clone can be correlated by index.
    /// </summary>
    private static void CollectImages(DependencyObject node, List<Image> images)
    {
        if (node is Image image)
            images.Add(image);

        foreach (var child in LogicalTreeHelper.GetChildren(node))
            if (child is DependencyObject d)
                CollectImages(d, images);
    }

    /// <summary>
    /// Re-attaches the original <see cref="ImageSource"/>s onto the cloned document's images.
    /// An <see cref="ImageSource"/> is shareable across elements on the same thread, so the clone
    /// reuses the very bitmaps the editor decoded — no pixel copy, no fidelity loss.
    /// </summary>
    private static void RestoreImageSources(DependencyObject cloneRoot, List<ImageSource?> sources)
    {
        if (sources.Count == 0)
            return;

        var cloned = new List<Image>();
        CollectImages(cloneRoot, cloned);
        // Defensive: if the traversals ever disagree, restore the prefix they do agree on rather than
        // pairing the wrong bitmap with the wrong placeholder.
        for (var i = 0; i < cloned.Count && i < sources.Count; i++)
            cloned[i].Source = sources[i];
    }

    /// <summary>
    /// Recursively clears every non-null <c>Tag</c> in the logical tree, recording each so
    /// <see cref="CloneElement"/> can restore them after serialization.
    /// </summary>
    private static void StripTags(DependencyObject node, List<(DependencyObject Node, object Tag)> saved)
    {
        if (GetTag(node) is { } tag)
        {
            saved.Add((node, tag));
            SetTag(node, null);
        }

        foreach (var child in LogicalTreeHelper.GetChildren(node))
            if (child is DependencyObject d)
                StripTags(d, saved);
    }

    // Tag lives on both FrameworkElement and FrameworkContentElement as distinct properties; handle both.
    private static object? GetTag(DependencyObject node) => node switch
    {
        FrameworkElement fe => fe.Tag,
        FrameworkContentElement fce => fce.Tag,
        _ => null
    };

    private static void SetTag(DependencyObject node, object? tag)
    {
        if (node is FrameworkElement fe) fe.Tag = tag;
        else if (node is FrameworkContentElement fce) fce.Tag = tag;
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
internal sealed class HeaderFooterPaginator(
    DocumentPaginator inner,
    TextDocument model,
    PageSettings page,
    double lineHeightDip = 0,
    IReadOnlyList<IReadOnlyList<int>>? footnoteIdsByPage = null,
    IReadOnlyDictionary<int, DocumentFootnoteContinuationPagePlan>? fragmentedFootnotePages = null,
    IReadOnlyList<ReviewBalloonSource>? balloonSources = null) : DocumentPaginator
{
    private bool? _requiresDedicatedEndnotePage;

    public override bool IsPageCountValid => inner.IsPageCountValid;
    public override int PageCount => inner.PageCount + (RequiresDedicatedEndnotePage ? 1 : 0);
    public override Size PageSize
    {
        get => inner.PageSize;
        set
        {
            inner.PageSize = value;
            _requiresDedicatedEndnotePage = null;
        }
    }
    public override IDocumentPaginatorSource Source => inner.Source;

    public override DocumentPage GetPage(int pageNumber)
    {
        if (RequiresDedicatedEndnotePage && pageNumber == inner.PageCount)
            return BuildDedicatedEndnotePage(pageNumber);

        var basePage = inner.GetPage(pageNumber);
        var hasWatermark = !string.IsNullOrEmpty(page.Watermark);
        var pageBorder = page.PageBorder;
        var hasBorder = pageBorder is not null
            && PageBorderVisibilityPlanner.ShouldRender(pageBorder.Display, pageNumber);
        var hasLineNumbers = page.LineNumberMode != LineNumberMode.None && lineHeightDip > 0;
        var hasColumnRule = page.ColumnsLineBetween && page.ColumnCount > 1;
        // Footnote bodies follow the page containing their reference. Markerless single-page models
        // retain the historical fallback that displays all stored notes on that page.
        var resolvedFootnoteIdsByPage = footnoteIdsByPage ?? BuildFootnoteIdsByPage(model, inner);
        var pageFootnoteIds = pageNumber < resolvedFootnoteIdsByPage.Count
            ? resolvedFootnoteIdsByPage[pageNumber]
            : null;
        var fragmentedFootnotePage = fragmentedFootnotePages?.GetValueOrDefault(pageNumber);
        var hasMappedNotesAtFoot = pageFootnoteIds is { Count: > 0 };
        var hasAnyMappedFootnotes = resolvedFootnoteIdsByPage.Any(ids => ids.Count > 0);
        var hasFallbackNotesAtFoot = !hasAnyMappedFootnotes
            && inner.PageCount == 1
            && (model.Footnotes.Count > 0 || model.Endnotes.Count > 0);
        // Endnotes are collected at the document end. They belong on the final printed page even
        // when the document has multiple body pages and no footnote marker assignment on that page.
        var hasEndnotesAtFoot = model.Endnotes.Count > 0
            && !RequiresDedicatedEndnotePage
            && pageNumber == inner.PageCount - 1;
        var hasNotesAtFoot = fragmentedFootnotePage is not null
            || hasMappedNotesAtFoot
            || hasFallbackNotesAtFoot
            || hasEndnotesAtFoot;
        // Review > Show Markup > Balloons: assign each balloon to a page by its source paragraph's
        // proportional position in the document. There is no block-to-page map available here (the
        // same approximation BalloonOverlay already uses for the live, continuously-scrolled strip),
        // but it keeps a single-page document's balloons on that page and spreads a long document's
        // balloons roughly with the content they annotate.
        var pageBalloons = ResolvePageBalloons(pageNumber);
        var hasBalloons = pageBalloons.Count > 0;
        if (model.Header is not { IsEmpty: false } && model.Footer is not { IsEmpty: false }
            && !hasWatermark && !hasBorder && !hasLineNumbers && !hasColumnRule && !hasNotesAtFoot
            && !hasBalloons)
            return basePage;

        var size = basePage.Size;
        var visual = new ContainerVisual();
        // The watermark sits behind page content. Page-border z-order decides which side of body text owns it.
        if (hasWatermark)
            visual.Children.Add(BuildWatermark(page.Watermark!, size));
        if (hasBorder
            && PageBorderVisibilityPlanner.LayerFor(pageBorder!.ZOrder) == PageBorderRenderLayer.BehindText)
            visual.Children.Add(BuildPageBorder(pageBorder, size));
        // PdfExport queries the same paginator twice per page (once to recover XPS text overlays, once
        // to rasterize), and the inner FlowDocumentPaginator can hand back the identical cached
        // DocumentPage/Visual for a repeat GetPage(pageNumber) call. Once this method has already wrapped
        // that Visual into a page's ContainerVisual, re-adding it to a second wrapper without detaching
        // first throws "Specified Visual is already a child of another Visual" -- this went unnoticed
        // because only header/footer/watermark/border/notes ever reached this wrap branch before; a
        // document whose only reason to wrap is the Review > Show Markup > Balloons strip (R139) hits it
        // on the very first such print/PDF/XPS export. Detach from whatever wrapper produced it before.
        if (basePage.Visual is not null
            && VisualTreeHelper.GetParent(basePage.Visual) is ContainerVisual previousWrapper)
            previousWrapper.Children.Remove(basePage.Visual);
        visual.Children.Add(basePage.Visual);
        if (hasBorder
            && PageBorderVisibilityPlanner.LayerFor(pageBorder!.ZOrder) == PageBorderRenderLayer.InFrontOfText)
            visual.Children.Add(BuildPageBorder(pageBorder, size));
        if (hasColumnRule)
            visual.Children.Add(DocumentView.BuildColumnRuleVisual(
                page,
                PageLayout.PointsToDip(page.MarginLeftPt),
                PageLayout.PointsToDip(page.MarginTopPt),
                size.Width - PageLayout.PointsToDip(page.MarginLeftPt) - PageLayout.PointsToDip(page.MarginRightPt),
                size.Height - PageLayout.PointsToDip(page.MarginBottomPt)));
        if (hasLineNumbers)
            visual.Children.Add(BuildLineNumbers(basePage, pageNumber));

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

        if (hasNotesAtFoot)
        {
            visual.Children.Add(fragmentedFootnotePage is not null
                ? BuildContinuationNotesAtFoot(size, marginLeft, contentWidth, fragmentedFootnotePage)
                : BuildNotesAtFoot(
                    size,
                    marginLeft,
                    contentWidth,
                    pageFootnoteIds,
                    includeAllNotes: hasFallbackNotesAtFoot,
                    includeEndnotes: !RequiresDedicatedEndnotePage && pageNumber == inner.PageCount - 1));
        }

        if (hasBalloons)
            visual.Children.Add(BuildBalloonStrip(pageBalloons, size));

        return new DocumentPage(visual, size, basePage.BleedBox, basePage.ContentBox);
    }

    /// <summary>
    /// The balloon sources (comments/tracked-change revisions) assigned to <paramref name="pageNumber"/>,
    /// by proportionally mapping each source's paragraph index over the document's total paragraph
    /// count onto <see cref="PageCount"/>. Empty when balloons mode is off or nothing was assigned.
    /// </summary>
    private IReadOnlyList<ReviewBalloonSource> ResolvePageBalloons(int pageNumber)
    {
        if (balloonSources is not { Count: > 0 })
            return [];

        var totalPages = Math.Max(1, PageCount);
        var totalBlocks = Math.Max(1, model.Blocks.Count);
        List<ReviewBalloonSource>? matches = null;
        foreach (var source in balloonSources)
        {
            var targetPage = Math.Clamp(
                (int)((double)source.BlockIndex / totalBlocks * totalPages),
                0,
                totalPages - 1);
            if (targetPage != pageNumber)
                continue;

            matches ??= [];
            matches.Add(source);
        }

        return matches is null ? [] : matches;
    }

    /// <summary>
    /// Draws the right-margin balloon strip for one page: a shaded strip, a dashed leader line per
    /// balloon back toward the body content, and a rounded card carrying author/kind, resolved/reply
    /// metadata, and a truncated body preview — the same information <c>BalloonOverlay</c> shows on
    /// screen for Review &gt; Show Markup &gt; Balloons, laid out via the shared
    /// <see cref="ReviewBalloonLayoutPlanner"/> so print/PDF/XPS matches the interactive strip's rules.
    /// </summary>
    private static DrawingVisual BuildBalloonStrip(IReadOnlyList<ReviewBalloonSource> sources, Size size)
    {
        var visual = new DrawingVisual();
        if (sources.Count == 0)
            return visual;

        var options = new ReviewBalloonLayoutOptions();
        var stripLeft = Math.Max(0, size.Width - options.StripWidth);
        var layouts = ReviewBalloonLayoutPlanner.BuildLayout(sources, size.Height, options);

        using var dc = visual.RenderOpen();
        dc.DrawRectangle(
            ToBrush(ReviewBalloonStyleCatalog.PaneBackground),
            null,
            new Rect(stripLeft, 0, options.StripWidth, size.Height));

        var leaderPen = new Pen(ToBrush(ReviewBalloonStyleCatalog.Leader), 0.75)
        {
            DashStyle = DashStyles.Dash
        };
        foreach (var layout in layouts)
        {
            dc.DrawLine(
                leaderPen,
                new Point(stripLeft + layout.LeaderStartX, layout.LeaderStartY),
                new Point(stripLeft + layout.LeaderEndX, layout.LeaderEndY));

            var style = ReviewBalloonStyleCatalog.Resolve(layout.Source.Kind, layout.Source.Resolved);
            var rect = new Rect(stripLeft + layout.BalloonX, layout.BalloonY, layout.BalloonWidth, layout.BalloonHeight);
            dc.DrawRoundedRectangle(
                ToBrush(style.Fill),
                new Pen(ToBrush(style.Stroke), 1.0),
                rect,
                options.BalloonCornerRadius,
                options.BalloonCornerRadius);

            DrawBalloonText(
                dc,
                $"{layout.Source.HeaderText} – {layout.Source.KindLabel}",
                ReviewBalloonStyleCatalog.AuthorText,
                bold: true,
                new Point(rect.X + 4, rect.Y + 3),
                rect.Width - 8);
            DrawBalloonText(
                dc,
                layout.Source.MetadataText,
                ReviewBalloonStyleCatalog.MetadataText,
                bold: false,
                new Point(rect.X + 4, rect.Y + 15),
                rect.Width - 8);
            var preview = ReviewBalloonLayoutPlanner.TruncatePreview(layout.Source.BodyText, 90, "…");
            if (!string.IsNullOrEmpty(preview))
            {
                DrawBalloonText(
                    dc,
                    preview,
                    ReviewBalloonStyleCatalog.BodyText,
                    bold: false,
                    new Point(rect.X + 4, rect.Y + 27),
                    rect.Width - 8,
                    maxLines: 2);
            }
        }

        return visual;
    }

    private static void DrawBalloonText(
        DrawingContext dc,
        string text,
        ReviewBalloonColor color,
        bool bold,
        Point origin,
        double width,
        int maxLines = 1)
    {
        if (string.IsNullOrEmpty(text) || width <= 0)
            return;

        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Calibri"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            PageLayout.PointsToDip(bold ? 7.5 : 7.0),
            ToBrush(color),
            1.0)
        {
            MaxTextWidth = width,
            MaxLineCount = maxLines,
            Trimming = TextTrimming.CharacterEllipsis
        };

        dc.DrawText(formatted, origin);
    }

    private static Brush ToBrush(ReviewBalloonColor color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Draws footnote/endnote bodies at the foot of a single-page document: a short separator rule just
    /// below the content area, then each note as "N. text" in 9pt, stacked into the bottom margin. Word
    /// reserves space inside the content area for these; FreeW approximates by drawing them in the
    /// otherwise-empty bottom margin so the note text is visible (previously only the reference marker was).
    /// </summary>
    private DrawingVisual BuildNotesAtFoot(
        Size size,
        double marginLeft,
        double contentWidth,
        IReadOnlyList<int>? pageFootnoteIds,
        bool includeAllNotes,
        bool includeEndnotes,
        double? separatorYOverride = null,
        double? maxYOverride = null)
    {
        var visual = new DrawingVisual();
        if (contentWidth <= 0)
            return visual;

        // Print/Print Preview/PDF/XPS must show the same computed display sequence the body reference
        // marks and the interactive page-box note region show — not the note's raw internal dictionary
        // key, which stays fixed across deletions while the visible sequence shifts. Routing through
        // DocumentNoteRegionPlanner also picks up NoteNumberingOptions (Roman/letter/Chicago-symbol
        // formats, a non-1 Start At) and NumberRestart (EachSection/EachPage), which a bare id can
        // never reflect.
        var footnoteIds = includeAllNotes
            ? model.Footnotes.Keys.OrderBy(id => id).ToList()
            : model.Footnotes.Keys
                .Where(id => pageFootnoteIds?.Contains(id) == true)
                .OrderBy(id => id)
                .ToList();
        var noteRows = DocumentNoteRegionPlanner
            .BuildFootnoteRegion(model, footnoteIds, pageNumber: 1, contentWidth)
            .Rows;
        if (includeEndnotes)
        {
            var endnoteIds = model.Endnotes.Keys.OrderBy(id => id).ToList();
            noteRows = noteRows
                .Concat(DocumentNoteRegionPlanner
                    .BuildEndnoteRegion(model, endnoteIds, pageNumber: 1, contentWidth, isSyntheticPage: false)
                    .Rows)
                .ToList();
        }

        // DocumentNoteRegionPlanner.BuildRows already skips ids with no note at all -- every row that
        // reaches here is a note that genuinely exists, even if its content is empty. Word still prints
        // the separator plus a blank numbered line for an empty note, matching the interactive Page
        // Layout view and the footnote-continuation path, so this must not filter rows out by text.
        var notes = noteRows
            .Select(row => (
                Label: string.IsNullOrEmpty(row.Label)
                    ? row.NoteId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : row.Label,
                row.Text))
            .ToList();
        if (notes.Count == 0)
            return visual;

        var contentBottom = size.Height - PageLayout.PointsToDip(page.MarginBottomPt);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)), 0.5);
        using var dc = visual.RenderOpen();
        var sepY = separatorYOverride ?? contentBottom + PageLayout.PointsToDip(3);
        dc.DrawLine(pen, new Point(marginLeft, sepY), new Point(marginLeft + contentWidth * 0.3, sepY));

        var y = sepY + PageLayout.PointsToDip(2);
        var maxY = maxYOverride ?? size.Height - PageLayout.PointsToDip(4); // stay on the sheet
        foreach (var (label, text) in notes)
        {
            if (y >= maxY)
                break;
            var formatted = new FormattedText(
                $"{label}. {text}",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Calibri"),
                PageLayout.PointsToDip(9.0),
                Brushes.Black,
                1.0)
            {
                MaxTextWidth = contentWidth,
                MaxLineCount = 2,
                Trimming = TextTrimming.CharacterEllipsis
            };
            dc.DrawText(formatted, new Point(marginLeft, y));
            y += formatted.Height + PageLayout.PointsToDip(1);
        }
        return visual;
    }

    private DrawingVisual BuildContinuationNotesAtFoot(
        Size size,
        double marginLeft,
        double contentWidth,
        DocumentFootnoteContinuationPagePlan continuationPage)
    {
        var visual = new DrawingVisual();
        if (contentWidth <= 0)
            return visual;

        var plan = DocumentNoteRegionPlanner.BuildFootnoteContinuationRegion(
            continuationPage,
            contentWidth);
        const double trailingReserveDip = 15.0;
        var contentTop = PageLayout.PointsToDip(page.MarginTopPt);
        var contentBottom = size.Height - PageLayout.PointsToDip(page.MarginBottomPt);
        var y = Math.Max(contentTop, contentBottom - plan.EstimatedHeightDip - trailingReserveDip);
        using var dc = visual.RenderOpen();
        if (continuationPage.SeparatorKind is not DocumentFootnoteSeparatorKind.None)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)), 0.5);
            dc.DrawLine(
                pen,
                new Point(marginLeft + plan.SeparatorXOffsetDip, y),
                new Point(marginLeft + plan.SeparatorXOffsetDip + plan.SeparatorWidthDip, y));
            y += 8;
        }

        foreach (var row in plan.Rows)
        {
            var label = string.IsNullOrEmpty(row.Label) ? string.Empty : $"{row.Label}. ";
            var formatted = new FormattedText(
                label + row.Text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Calibri"),
                PageLayout.PointsToDip(plan.TextFontSizePt),
                Brushes.Black,
                1.0)
            {
                MaxTextWidth = contentWidth,
                Trimming = TextTrimming.None
            };
            dc.DrawText(formatted, new Point(marginLeft, y));
            y += Math.Max(row.EstimatedHeightDip, formatted.Height);
        }
        return visual;
    }

    internal bool RequiresDedicatedEndnotePage
    {
        get
        {
            if (_requiresDedicatedEndnotePage is { } cached)
                return cached;

            inner.ComputePageCount();
            if (model.Endnotes.Count == 0 || inner.PageCount == 0)
            {
                _requiresDedicatedEndnotePage = false;
                return false;
            }

            var finalPage = inner.GetPage(inner.PageCount - 1);
            var size = finalPage.Size;
            var width = Math.Max(1, (int)Math.Ceiling(size.Width));
            var height = Math.Max(1, (int)Math.Ceiling(size.Height));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                dc.DrawRectangle(
                    new VisualBrush(finalPage.Visual) { Stretch = Stretch.None },
                    null,
                    new Rect(0, 0, size.Width, size.Height));
            }
            bitmap.Render(visual);

            var marginLeft = PageLayout.PointsToDip(page.MarginLeftPt);
            var marginRight = PageLayout.PointsToDip(page.MarginRightPt);
            var contentWidth = Math.Max(0, size.Width - marginLeft - marginRight);
            var notePlan = DocumentNoteRegionPlanner.BuildEndnoteRegion(
                model,
                model.Endnotes.Keys.OrderBy(id => id).ToList(),
                pageNumber: inner.PageCount,
                contentWidth,
                isSyntheticPage: false);
            var contentBottom = size.Height - PageLayout.PointsToDip(page.MarginBottomPt);
            var nextContentY = Math.Max(
                PageLayout.PointsToDip(page.MarginTopPt),
                FindLastPaintedRow(bitmap) + 16);
            _requiresDedicatedEndnotePage = nextContentY + notePlan.EstimatedHeightDip > contentBottom;
            return _requiresDedicatedEndnotePage.Value;
        }
    }

    private DocumentPage BuildDedicatedEndnotePage(int pageNumber)
    {
        var size = inner.PageSize;
        var visual = new ContainerVisual();
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
            dc.DrawRectangle(Brushes.White, null, new Rect(new Point(), size));
        visual.Children.Add(background);

        if (!string.IsNullOrEmpty(page.Watermark))
            visual.Children.Add(BuildWatermark(page.Watermark!, size));
        var border = page.PageBorder;
        var hasBorder = border is not null
            && PageBorderVisibilityPlanner.ShouldRender(border.Display, pageNumber);
        if (hasBorder
            && PageBorderVisibilityPlanner.LayerFor(border!.ZOrder) == PageBorderRenderLayer.BehindText)
            visual.Children.Add(BuildPageBorder(border, size));

        var marginLeft = PageLayout.PointsToDip(page.MarginLeftPt);
        var contentWidth = Math.Max(0, size.Width - marginLeft - PageLayout.PointsToDip(page.MarginRightPt));
        if (model.Header is { IsEmpty: false } header)
        {
            var top = PageLayout.PointsToDip(Math.Max(0, page.MarginTopPt - 36));
            visual.Children.Add(BuildOverlay(ResolveText(header, pageNumber), marginLeft, top, contentWidth));
        }
        if (model.Footer is { IsEmpty: false } footer)
        {
            var bottom = size.Height - PageLayout.PointsToDip(Math.Max(18, page.MarginBottomPt - 18));
            visual.Children.Add(BuildOverlay(ResolveText(footer, pageNumber), marginLeft, bottom, contentWidth));
        }

        visual.Children.Add(BuildNotesAtFoot(
            size,
            marginLeft,
            contentWidth,
            pageFootnoteIds: null,
            includeAllNotes: false,
            includeEndnotes: true,
            separatorYOverride: PageLayout.PointsToDip(page.MarginTopPt) + 7,
            maxYOverride: size.Height - PageLayout.PointsToDip(page.MarginBottomPt)));

        if (hasBorder
            && PageBorderVisibilityPlanner.LayerFor(border!.ZOrder) == PageBorderRenderLayer.InFrontOfText)
            visual.Children.Add(BuildPageBorder(border, size));

        var contentBox = new Rect(
            marginLeft,
            PageLayout.PointsToDip(page.MarginTopPt),
            contentWidth,
            Math.Max(0, size.Height
                - PageLayout.PointsToDip(page.MarginTopPt)
                - PageLayout.PointsToDip(page.MarginBottomPt)));
        return new DocumentPage(visual, size, new Rect(new Point(), size), contentBox);
    }

    private static int FindLastPaintedRow(RenderTargetBitmap bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        for (var y = bitmap.PixelHeight - 1; y >= 0; y--)
        {
            var row = y * stride;
            for (var x = 0; x < bitmap.PixelWidth; x++)
            {
                var offset = row + x * 4;
                if (pixels[offset] < 245 || pixels[offset + 1] < 245 || pixels[offset + 2] < 245)
                    return y;
            }
        }
        return 0;
    }

    private static IReadOnlyList<IReadOnlyList<int>> BuildFootnoteIdsByPage(
        TextDocument model,
        IReadOnlyList<int> blockPageAssignment,
        int pageCount)
    {
        var mutablePages = Enumerable.Range(0, Math.Max(1, pageCount))
            .Select(_ => new List<int>())
            .ToList();

        for (var blockIndex = 0; blockIndex < model.Blocks.Count; blockIndex++)
        {
            var pageIndex = blockIndex < blockPageAssignment.Count
                ? Math.Clamp(blockPageAssignment[blockIndex], 0, mutablePages.Count - 1)
                : 0;
            foreach (var footnoteId in FootnoteIds(model.Blocks[blockIndex]))
            {
                if (!mutablePages[pageIndex].Contains(footnoteId))
                    mutablePages[pageIndex].Add(footnoteId);
            }
        }

        return mutablePages;
    }

    private static IReadOnlyList<IReadOnlyList<int>> BuildFootnoteIdsByPage(
        TextDocument model,
        DocumentPaginator paginator)
    {
        if (paginator.Source is not FlowDocument flow
            || paginator is not DynamicDocumentPaginator dynamicPaginator)
            return [];

        var assignment = new int[model.Blocks.Count];
        var flowBlocks = flow.Blocks.ToArray();
        var pageCount = Math.Max(1, paginator.PageCount);
        var currentPage = 0;
        for (var blockIndex = 0; blockIndex < model.Blocks.Count && blockIndex < flowBlocks.Length; blockIndex++)
        {
            var markerPositions = DocumentView.CollectFootnoteMarkerPositions([flowBlocks[blockIndex]]);
            try
            {
                var page = markerPositions.Count > 0
                    ? markerPositions
                        .Select(dynamicPaginator.GetPageNumber)
                        .Where(pageNumber => pageNumber >= 0)
                        .DefaultIfEmpty(dynamicPaginator.GetPageNumber(flowBlocks[blockIndex].ContentStart))
                        .Max()
                    : dynamicPaginator.GetPageNumber(flowBlocks[blockIndex].ContentStart);
                if (page >= 0)
                    currentPage = Math.Clamp(page, 0, pageCount - 1);
            }
            catch (NotSupportedException) { }
            catch (InvalidOperationException) { }

            assignment[blockIndex] = currentPage;
        }

        return BuildFootnoteIdsByPage(model, assignment, pageCount);
    }

    private static IEnumerable<int> FootnoteIds(FreeW.Core.Model.Block block)
    {
        switch (block)
        {
            case FreeW.Core.Model.Paragraph paragraph:
                foreach (var run in paragraph.Runs)
                    if (run.FootnoteId is { } id)
                        yield return id;
                break;

            case FreeW.Core.Model.Table table:
                foreach (var row in table.Rows)
                    foreach (var cell in row.Cells)
                        foreach (var paragraph in cell.Paragraphs)
                            foreach (var run in paragraph.Runs)
                                if (run.FootnoteId is { } id)
                                    yield return id;
                break;
        }
    }

    /// <summary>
    /// Draws the whole-page border (w:pgBorders) as a rectangle just inside the page edge, matching the
    /// editor's BorderBrush/BorderThickness chrome so on-screen and printed pages agree.
    /// </summary>
    private DrawingVisual BuildPageBorder(PageBorder border, Size size)
    {
        var visual = new DrawingVisual();
        var color = ParseColor(border.ColorHex);
        var thickness = Math.Max(1, PageLayout.PointsToDip(border.WidthPt));
        Rect frame;
        double artInset;
        if (border.OffsetFrom == PageBorderOffsetFrom.Text)
        {
            var planned = PageBorderTextFramePlanner.Build(
                page,
                border,
                size.Width,
                size.Height,
                PageLayout.DipPerPoint,
                thickness,
                model.PageBordersDoNotSurroundHeader,
                model.PageBordersDoNotSurroundFooter);
            frame = new Rect(planned.X, planned.Y, planned.Width, planned.Height);
            artInset = 0;
        }
        else
        {
            frame = new Rect(0, 0, size.Width, size.Height);
            artInset = Math.Min(
                PageLayout.PointsToDip(Math.Max(0, border.SpacePt)),
                Math.Min(size.Width, size.Height) / 4);
        }

        using (var artContext = visual.RenderOpen())
        {
            if (PageBorderArtWpfRenderer.TryDraw(
                    artContext,
                    border,
                    frame,
                    artInset))
            {
                return visual;
            }
        }

        if (border.LineStyle == BorderLineStyle.Wave)
        {
            var waveColor = Color.FromArgb(
                (byte)Math.Round(255 * PageBorderWaveVisualPlanner.StrokeOpacity),
                color.R,
                color.G,
                color.B);
            var wavePen = new Pen(
                new SolidColorBrush(waveColor),
                PageBorderWaveVisualPlanner.StrokeWidthDip);
            using var waveContext = visual.RenderOpen();
            foreach (var segment in PageBorderWaveVisualPlanner.BuildFrame(frame.Width, frame.Height, artInset))
            {
                waveContext.DrawLine(
                    wavePen,
                    new Point(frame.X + segment.X1Dip, frame.Y + segment.Y1Dip),
                    new Point(frame.X + segment.X2Dip, frame.Y + segment.Y2Dip));
            }

            return visual;
        }

        var pen = new Pen(new SolidColorBrush(color), thickness);
        Rect rect;
        if (border.OffsetFrom == PageBorderOffsetFrom.Text)
        {
            var half = thickness / 2;
            rect = new Rect(
                frame.X + half,
                frame.Y + half,
                Math.Max(0, frame.Width - thickness),
                Math.Max(0, frame.Height - thickness));
        }
        else
        {
            // Inset by half the stroke width plus the 24pt offsetFrom="page" gap used on save, clamped so
            // the rectangle stays positive on small pages.
            var inset = thickness / 2 + Math.Min(PageLayout.PointsToDip(24), Math.Min(size.Width, size.Height) / 4);
            rect = new Rect(inset, inset, Math.Max(0, size.Width - 2 * inset), Math.Max(0, size.Height - 2 * inset));
        }
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(null, pen, rect);
        return visual;
    }

    /// <summary>
    /// Draws line numbers down the left margin of a page (w:lnNumType). Lines are detected best-effort:
    /// the page's text content box (the laid-out content area for this page) is divided by the
    /// estimated line height to get how many text lines this page holds, and a number is drawn at the
    /// baseline of each one. Only every Nth line (countBy) shows a number. For RestartEachPage the count
    /// restarts at 1 on each page; for Continuous the start is carried forward by estimating a uniform
    /// lines-per-page from the printable height (pageNumber * linesPerPage + local index).
    /// </summary>
    private DrawingVisual BuildLineNumbers(DocumentPage basePage, int zeroBasedPageNumber)
    {
        var visual = new DrawingVisual();
        var content = basePage.ContentBox;
        if (content.Height <= 0 || lineHeightDip <= 0)
            return visual;

        var linesThisPage = Math.Max(1, (int)Math.Floor(content.Height / lineHeightDip));
        var countBy = Math.Max(1, page.LineNumberCountBy);

        // For continuous numbering, estimate how many lines a full printable column holds so prior
        // pages' line counts can be carried forward without re-paginating them.
        var printableHeightDip = PageLayout.PointsToDip(
            page.HeightPt - page.MarginTopPt - page.MarginBottomPt);
        var linesPerPage = Math.Max(1, (int)Math.Floor(printableHeightDip / lineHeightDip));
        var startLine = page.LineNumberMode == LineNumberMode.RestartEachPage
            ? 0
            : zeroBasedPageNumber * linesPerPage;

        // Place numbers just left of the content box (in the left margin), right-aligned to a small gutter.
        var gutterRight = Math.Max(0, content.Left - PageLayout.PointsToDip(6));

        using var dc = visual.RenderOpen();
        for (var i = 0; i < linesThisPage; i++)
        {
            var lineNumber = startLine + i + 1; // 1-based
            if (lineNumber % countBy != 0)
                continue;

            var formatted = new FormattedText(
                lineNumber.ToString(System.Globalization.CultureInfo.CurrentCulture),
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Calibri"),
                PageLayout.PointsToDip(9.0),
                new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
                1.0);

            var y = content.Top + i * lineHeightDip;
            var x = Math.Max(0, gutterRight - formatted.Width);
            dc.DrawText(formatted, new Point(x, y));
        }

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
        => WpfRgbColorAdapter.ParseDrawingMlOrDefault(hex, Colors.Black);

    /// <summary>Renders one line of header/footer text into a positioned drawing visual.</summary>
    private static DrawingVisual BuildOverlay(string text, double x, double y, double width)
    {
        var visual = new DrawingVisual();
        // No text, or no usable content width (margins meet/exceed the page width): nothing to draw.
        // MaxTextWidth must stay finite — WPF's text formatter throws ArgumentOutOfRangeException
        // ("paragraphWidth '∞'") if it is set to PositiveInfinity, which previously crashed the whole
        // print/preview paginator for narrow-content header/footer pages.
        if (string.IsNullOrEmpty(text) || width <= 0)
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
            MaxTextWidth = width,
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
        => HeaderFooterVisualPlanner.ResolveLineText(
            content,
            new HeaderFooterFieldResolutionContext(
                model,
                (zeroBasedPageNumber + 1).ToString(System.Globalization.CultureInfo.CurrentCulture),
                inner.PageCount,
                SectionOrdinal: 1,
                SectionPageCount: inner.PageCount,
                EvaluatedAt: DateTime.Now,
                Culture: System.Globalization.CultureInfo.CurrentCulture));
}
