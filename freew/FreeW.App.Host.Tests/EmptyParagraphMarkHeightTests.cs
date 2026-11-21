using System;
using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// An empty paragraph has the height of its paragraph mark's line in Word (a 28pt mark is a 28pt line),
/// even when the document font is much smaller. WPF sizes an empty paragraph's line from the
/// paragraph's own font, so the rendered height must follow the mark formatting. Runs on STA.
/// </summary>
public sealed class EmptyParagraphMarkHeightTests
{
    private const double PxPerPoint = 96.0 / 72.0;
    private static string Dbg = "";

    private static double PageContentHeight(double markSizePt, string family, bool leadingText)
    {
        var doc = TextDocument.CreateEmpty();
        doc.DefaultRun = doc.DefaultRun with { FontFamily = "Calibri", FontSizePt = 11 };
        doc.Blocks.Clear();
        var spacing = ParagraphFormatting.Default with
        {
            LineSpacing = 1.0,
            LineRule = LineSpacingRule.Multiple,
            LineSpacingIsSet = true,
            SpaceAfterPt = 0,
        };
        if (leadingText)
            doc.Blocks.Add(new Paragraph("lead") { Formatting = spacing });
        if (markSizePt > 0)
        {
            doc.Blocks.Add(new Paragraph(string.Empty)
            {
                Formatting = spacing,
                MarkFormatting = RunFormatting.Default with { FontFamily = family, FontSizePt = markSizePt },
            });
        }

        doc.Blocks.Add(new Paragraph("text") { Formatting = spacing });

        var view = new DocumentView();
        view.LoadModel(doc);
        var flow = view.Document;
        Dbg = string.Join(";", flow.Blocks.OfType<System.Windows.Documents.Paragraph>().Select(q => $"{q.FontSize}/{q.LineHeight}/{q.FontFamily}/{q.Margin}"));
        view.Document = new FlowDocument();
        flow.PageWidth = 794;
        flow.PageHeight = 1123;
        flow.PagePadding = new System.Windows.Thickness(48);
        var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        paginator.PageSize = new System.Windows.Size(794, 1123);
        return LastBaselineY(paginator.GetPage(0).Visual);
    }

    private static double LastBaselineY(System.Windows.Media.Visual visual)
    {
        var max = double.NaN;
        Collect(visual, 0, ref max);
        return max;
    }

    private static void Collect(System.Windows.Media.Visual visual, double offsetY, ref double max)
    {
        offsetY += System.Windows.Media.VisualTreeHelper.GetOffset(visual).Y;
        if (System.Windows.Media.VisualTreeHelper.GetDrawing(visual) is { } drawing)
            Collect(drawing, offsetY, ref max);
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(visual); i++)
        {
            if (System.Windows.Media.VisualTreeHelper.GetChild(visual, i) is System.Windows.Media.Visual child)
                Collect(child, offsetY, ref max);
        }
    }

    private static void Collect(System.Windows.Media.Drawing drawing, double offsetY, ref double max)
    {
        if (drawing is System.Windows.Media.GlyphRunDrawing glyphs)
        {
            var y = offsetY + glyphs.GlyphRun.BaselineOrigin.Y;
            max = double.IsNaN(max) ? y : Math.Max(max, y);
        }
        else if (drawing is System.Windows.Media.DrawingGroup group)
        {
            foreach (var child in group.Children)
                Collect(child, offsetY, ref max);
        }
    }

    [StaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmptyParagraph_TakesItsLineFromTheParagraphMarkSize(bool leadingText)
    {
        var without = PageContentHeight(0, "Calibri", leadingText);
        var with = PageContentHeight(28, "Bookman Old Style", leadingText);
        var natural = new System.Windows.Media.FontFamily("Bookman Old Style").LineSpacing * 28 * PxPerPoint;

        Assert.True(with - without > natural * 0.95,
            $"28pt mark line must be about {natural:F1}px but added {with - without:F1}px {Dbg} w={with} wo={without}");
    }
}
