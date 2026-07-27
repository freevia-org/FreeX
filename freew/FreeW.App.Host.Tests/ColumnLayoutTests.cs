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
        bitmap.CopyPixels(new Int32Rect(408, 500, 1, 1), pixel, 4, 0);
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

        // The narrowest width drives the flexible column width so both columns fit the content area.
        Assert.False(double.IsPositiveInfinity(view.Document.ColumnWidth));
        Assert.True(view.Document.ColumnWidth > 0);
    }
}
