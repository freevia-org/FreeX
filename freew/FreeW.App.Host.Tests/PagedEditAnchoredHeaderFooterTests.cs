using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// The live editor's page view draws a header/footer's anchored content -- here Word's "Page Numbers
/// (Margins)" circle: a positioned legacy VML ellipse in the bottom margin holding a PAGE field -- at its
/// page position through the same <see cref="HeaderFooterBandRenderer"/> layout Print / Print Preview use,
/// instead of showing the text box's paragraph as a plain line of the footer strip.
/// </summary>
public sealed class PagedEditAnchoredHeaderFooterTests
{
    private static readonly LegacyTextBoxFrame CircleFrame = new(
        IsEllipse: true,
        WidthPt: 36,
        HeightPt: 36,
        FillColorHex: "#C00000",
        StrokeColorHex: "#C00000",
        StrokeWidthPt: 1,
        HorizontalAlignment: LegacyFrameHorizontalAlignment.Center,
        LeftPt: 0,
        HorizontalReference: LegacyFrameReference.Page,
        VerticalAlignment: LegacyFrameVerticalAlignment.Center,
        TopPt: 0,
        VerticalReference: LegacyFrameReference.BottomMarginArea,
        BehindText: false,
        TextAnchor: LegacyFrameTextAnchor.Middle);

    private static (TextDocument Document, Paragraph FramedParagraph) DocumentWithCircledPageNumber()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body text"));

        var footer = new HeaderFooter();
        footer.Paragraphs.Add(new Paragraph("Footer text"));
        var framed = new Paragraph { LegacyTextBoxFrame = CircleFrame };
        framed.Runs.Add(Run.PageNumberField());
        footer.Paragraphs.Add(framed);
        doc.Footer = footer;
        return (doc, framed);
    }

    private static PaginatedEditorPanel BuildPanel(TextDocument doc, out DocumentView editor)
    {
        editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();
        return PaginatedEditorPanel.Build(editor);
    }

    [StaFact]
    public void LegacyTextBoxInFooter_IsDrawnAtItsPagePosition_NotAsAStripLine()
    {
        var (doc, _) = DocumentWithCircledPageNumber();
        var panel = BuildPanel(doc, out _);
        var box = panel.PageBoxes[0];

        // The strip shows only the in-flow footer line.
        var stripText = new System.Windows.Documents.TextRange(
            box.FooterSubEditor!.Document.ContentStart,
            box.FooterSubEditor.Document.ContentEnd).Text.Trim();
        Assert.Equal("Footer text", stripText);

        // The circle is an anchored front layer of the page.
        var layer = Assert.Single(box.AnchoredHeaderFooterLayers);

        // ...painted inside the bottom margin, centred on the page, like Print Preview.
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(box.PageGeometry);
        var (_, _, _, marginBottom) = PageLayout.MarginsDip(box.PageGeometry);
        box.Measure(new Size(pageWidth + 2, double.PositiveInfinity));
        box.Arrange(new Rect(0, 0, pageWidth + 2, box.DesiredSize.Height));
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(pageWidth + 2),
            (int)Math.Ceiling(box.DesiredSize.Height),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(box);

        var circleDip = PageLayout.PointsToDip(CircleFrame.WidthPt);
        var sampleX = (int)Math.Round(pageWidth / 2 + circleDip / 2 - 5);
        var redRows = Enumerable.Range((int)(pageHeight - marginBottom), (int)marginBottom - 1)
            .Count(y => IsCircleRed(Pixel(bitmap, sampleX, y)));
        Assert.True(redRows > 0, "the circled page number is drawn in the bottom margin of the live page");
        Assert.NotNull(layer.Layer);
    }

    [StaFact]
    public void CommittingTheFooterStrip_KeepsTheLegacyTextBoxParagraphInPlace()
    {
        var (doc, framed) = DocumentWithCircledPageNumber();
        var panel = BuildPanel(doc, out var editor);
        var box = panel.PageBoxes[0];

        box.CommitFooterSlot(editor, box.FooterOwnerSectionHf ?? editor.Model.FinalSectionHeadersFooters);

        var footer = (box.FooterOwnerSectionHf ?? editor.Model.FinalSectionHeadersFooters).Footer!;
        Assert.Equal(2, footer.Paragraphs.Count);
        Assert.Equal("Footer text", string.Concat(footer.Paragraphs[0].Runs.Select(run => run.Text)));
        Assert.Same(CircleFrame, footer.Paragraphs[1].LegacyTextBoxFrame);
        Assert.Equal(framed.Runs.Single().FieldKind, footer.Paragraphs[1].Runs.Single().FieldKind);
    }

    [Fact]
    public void Splice_DropsTheStripPlaceholderWhenTheSlotHeldOnlyTextBoxes()
    {
        var framed = new Paragraph { LegacyTextBoxFrame = CircleFrame };
        var original = new HeaderFooter();
        original.Paragraphs.Add(framed);
        var committed = new System.Collections.Generic.List<Paragraph> { new() };

        PageBox.SpliceLegacyTextBoxParagraphs(original, committed);

        Assert.Same(framed, Assert.Single(committed));
    }

    private static Color Pixel(BitmapSource bitmap, int x, int y)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    private static bool IsCircleRed(Color color) => color.R > 150 && color.G < 80 && color.B < 80;
}
