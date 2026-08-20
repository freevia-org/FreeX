using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Verifies the editor flows body text into the page's multi-column layout: the rendered
/// <see cref="FlowDocument"/> picks up the column gap and a finite column width (so WPF lays out more
/// than one column). The visible rule is page chrome rather than WPF's native half-pixel flow rule.
/// Also checks that applying column settings mutates the model.
/// Runs on STA (WPF FlowDocument).
/// </summary>
public sealed class ColumnLayoutTests
{
    private static DocumentView ViewWith(PageSettings page)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body text that flows across the configured columns."));
        doc.Page.ColumnCount = page.ColumnCount;
        doc.Page.ColumnSpacingPt = page.ColumnSpacingPt;
        doc.Page.ColumnsLineBetween = page.ColumnsLineBetween;
        doc.Page.ColumnWidthsPt = page.ColumnWidthsPt;

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    [StaFact]
    public void SingleColumn_LeavesFlowSpanningFullWidth()
    {
        var view = ViewWith(new PageSettings { ColumnCount = 1 });

        Assert.True(double.IsPositiveInfinity(view.Document.ColumnWidth));
        Assert.Equal(0, view.Document.ColumnRuleWidth);
    }

    [StaFact]
    public void TwoColumns_FlowGetsFiniteWidthAndGap()
    {
        var view = ViewWith(new PageSettings { ColumnCount = 2, ColumnSpacingPt = 24 });

        Assert.False(double.IsPositiveInfinity(view.Document.ColumnWidth));
        Assert.True(view.Document.ColumnWidth > 0);
        Assert.True(view.Document.ColumnGap > 0);
    }

    [StaFact]
    public void LineBetween_ReservesTheRuleForPixelAlignedPageChrome()
    {
        var view = ViewWith(new PageSettings { ColumnCount = 2, ColumnsLineBetween = true });

        Assert.Equal(0, view.Document.ColumnRuleWidth);
    }

    [StaFact]
    public void LineBetween_AddsANonInteractivePixelAlignedAdornerInPrintLayout()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Column-rule overlay evidence."));
        doc.Page.ColumnCount = 2;
        doc.Page.ColumnsLineBetween = true;

        var view = new DocumentView();
        var host = new AdornerDecorator { Child = view };
        host.Measure(new Size(816, 1056));
        host.Arrange(new Rect(0, 0, 816, 1056));
        view.LoadModel(doc);
        host.UpdateLayout();

        var layer = AdornerLayer.GetAdornerLayer(view);
        var adorner = Assert.Single(
            layer?.GetAdorners(view) ?? [],
            candidate => candidate.GetType().Name == "ColumnRuleAdorner");
        Assert.False(adorner.IsHitTestVisible);

        var bitmap = new RenderTargetBitmap(816, 1056, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(407, 500, 1, 1), pixel, 4, 0);
        Assert.Equal(0, pixel[0]);
        Assert.Equal(0, pixel[1]);
        Assert.Equal(0, pixel[2]);
    }

    [StaFact]
    public void ApplyPageSettings_UpdatesModelColumns()
    {
        var view = ViewWith(new PageSettings { ColumnCount = 1 });

        view.ApplyPageSettings(page =>
        {
            page.ColumnCount = 3;
            page.ColumnSpacingPt = 18;
            page.ColumnsLineBetween = true;
        });

        Assert.Equal(3, view.Model.Page.ColumnCount);
        Assert.Equal(18, view.Model.Page.ColumnSpacingPt);
        Assert.True(view.Model.Page.ColumnsLineBetween);
        // The re-render picked up the new layout and leaves the visible divider to page chrome.
        Assert.Equal(0, view.Document.ColumnRuleWidth);
        Assert.False(double.IsPositiveInfinity(view.Document.ColumnWidth));
    }

    [StaFact]
    public void UnequalWidths_RenderMultipleColumns()
    {
        var view = ViewWith(new PageSettings { ColumnCount = 2, ColumnWidthsPt = [108.0, 360.0] });

        // The content area is split evenly across the flexible column width so both columns fit.
        Assert.False(double.IsPositiveInfinity(view.Document.ColumnWidth));
        Assert.True(view.Document.ColumnWidth > 0);
    }

    /// <summary>
    /// Builds the same paginated <see cref="FlowDocument"/> shape Print Preview/PDF/XPS export use
    /// (<c>PrintPreviewWindow.BuildPaginatedDocument</c>: page size and margins from
    /// <see cref="PageLayout"/>, columns from <see cref="DocumentView.ApplyColumnLayout"/>) and returns
    /// the count of distinct vertical ink bands rendered on page 0 -- i.e. the column count WPF's
    /// paginator actually produced, independent of what <see cref="PageSettings.ColumnCount"/> asked for.
    /// </summary>
    // Varied word lengths so wrapped lines break at different x offsets from one line to the next
    // (uniform-length filler words wrap identically on every line, leaving periodic, perfectly
    // aligned inter-word gaps that a per-column ink union mistakes for extra column boundaries).
    private static readonly string[] FillerWords =
    [
        "word", "sentence", "a", "flows", "across", "the", "configured", "columns", "of", "this",
        "printed", "page", "filling", "every", "available", "line", "completely", "so", "ink",
        "coverage", "varies", "enough", "to", "avoid", "a", "perfectly", "periodic", "wrap",
    ];

    private static int RenderedColumnBandCount(PageSettings page, int fillerWordCount = 1500)
    {
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(page);
        var (left, top, right, bottom) = PageLayout.MarginsDip(page);
        var flow = new FlowDocument
        {
            PageWidth = pageWidth,
            PageHeight = pageHeight,
            PagePadding = new Thickness(left, top, right, bottom),
        };
        // Enough repeated words to fill well past a single page's worth of columns so every column
        // (including any phantom extra one) actually receives ink. Fully qualified: this test
        // project's global usings alias the bare "Paragraph"/"Run" names to the FreeW.Core.Model
        // document types, not WPF's FlowDocument ones.
        var text = string.Join(" ", Enumerable.Range(0, fillerWordCount).Select(i => FillerWords[i % FillerWords.Length]));
        flow.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run(text)));
        DocumentView.ApplyColumnLayout(flow, page, useNativeColumnRule: false);

        var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        var docPage = paginator.GetPage(0);

        var widthPx = (int)Math.Ceiling(pageWidth);
        var heightPx = (int)Math.Ceiling(pageHeight);
        var bitmap = new RenderTargetBitmap(widthPx, heightPx, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(docPage.Visual);
        var stride = widthPx * 4;
        var pixels = new byte[stride * heightPx];
        bitmap.CopyPixels(pixels, stride, 0);

        // A pixel-column has ink if ANY row places a glyph over it. With varied-length filler words,
        // enough different lines wrap at enough different offsets that the only x-ranges left with no
        // ink at all across the whole page are the real inter-column gutters and the page margins.
        var hasInk = new bool[widthPx];
        for (var y = 0; y < heightPx; y++)
        {
            var rowOffset = y * stride;
            for (var x = 0; x < widthPx; x++)
            {
                var offset = rowOffset + x * 4;
                // BGRA: any non-transparent, non-white pixel counts as ink (rendered glyph antialiasing).
                if (pixels[offset + 3] > 0 && (pixels[offset] != 255 || pixels[offset + 1] != 255 || pixels[offset + 2] != 255))
                    hasInk[x] = true;
            }
        }

        var bandCount = 0;
        var inBand = false;
        var lastInkX = -1;
        // A gap narrower than half the narrowest column gap the tests configure (24dip -> 12dip
        // tolerance) is treated as still inside the same band; anything wider is a real gutter.
        const int mergeToleranceDip = 10;
        for (var x = 0; x < widthPx; x++)
        {
            if (!hasInk[x])
                continue;
            if (!inBand || x - lastInkX > mergeToleranceDip)
            {
                bandCount++;
                inBand = true;
            }
            lastInkX = x;
        }

        return bandCount;
    }

    [StaFact]
    public void UnequalWidths_LeftPreset_RendersExactlyTheConfiguredColumnCount()
    {
        // FreeW's built-in 'Left' Columns preset geometry on a default Letter page: narrow sidebar
        // 108pt, wide body = contentWidth(468pt) - spacing(36pt) - 108pt = 324pt.
        var page = new PageSettings
        {
            ColumnCount = 2,
            ColumnSpacingPt = 36,
            ColumnWidthsPt = [108.0, 324.0],
        };

        var bandCount = RenderedColumnBandCount(page);

        // Regression for freew-columns-flow F1: using the narrowest column width (108pt) as a
        // flexible FlowDocument.ColumnWidth left enough spare content-area room for WPF to pack in a
        // third, phantom column instead of the 2 the page settings ask for.
        Assert.Equal(2, bandCount);
    }

    [StaFact]
    public void EqualWidths_ThreeColumns_RenderExactlyTheConfiguredColumnCount()
    {
        // Sibling no-regression case: the far more common equal-width columns (no ColumnWidthsPt)
        // already used the evenly-split formula and must keep rendering the exact configured count.
        var page = new PageSettings
        {
            ColumnCount = 3,
            ColumnSpacingPt = 18,
        };

        var bandCount = RenderedColumnBandCount(page);

        Assert.Equal(3, bandCount);
    }
}
