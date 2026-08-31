using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Free.Shared.Pdf;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using SkiaSharp;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// sweep110 F1: BuildPdfEmbeddedObjectOps() used to center a fallback embedded-object label with a
/// flat "chars * fontSize * 0.52" width guess instead of the real glyph measurement the very same
/// class already computes (and already uses to center the identical label on screen, in
/// DrawInlineEmbeddedObject via the private Build(text, fmt) helper). These tests assert the PDF
/// export path and the on-screen measurement path agree, rather than merely asserting a substring
/// appears in the output.
/// </summary>
public sealed class R172_EmbeddedObjectPdfLabelCenteringTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static Task Dispatch(Action action) => Session.Dispatch(action, CancellationToken.None);

    private const double PxPerPoint = 96.0 / 72.0;

    [Fact]
    public async Task Fallback_label_is_centered_in_pdf_using_real_glyph_width_not_a_flat_char_count_guess()
    {
        await Dispatch(() =>
        {
            // "WWWWWWWWWW" is far wider per character than the flat 0.52em/char guess the old code
            // used, so the correct real-metrics centering and the old flat-guess centering disagree
            // by several points here -- enough to catch a regression back to the flat estimate.
            const string label = "WWWWWWWWWW";
            var fallbackObject = EmbeddedObject.Create([1, 2, 3], label, widthPt: 240, heightPt: 60);
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromEmbeddedObject(fallbackObject));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(900, 1200));
            view.Arrange(new Rect(0, 0, 900, 1200));
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

            var ops = view.BuildPdfContent().Pages.SelectMany(page => page.Ops).ToArray();
            var fillRect = ops.OfType<PdfFillRect>().Should().ContainSingle(fill =>
                fill.Color == new PdfColor(0xF3, 0xF6, 0xFB)).Subject;
            var text = ops.OfType<PdfText>().Should().ContainSingle(t => t.Text == label).Subject;

            // The same real-glyph measurement the live editor already uses to center this exact
            // label on screen (DrawInlineEmbeddedObject / Build), reached here via reflection since
            // it is a private instance method of DocumentView.
            var fmt = RunFormatting.Default with
            {
                FontSizePt = 10,
                ColorHex = EmbeddedObjectVisualPlanner.ForegroundColorHex,
            };
            var formatted = view.Build(label, fmt);
            var realWidthPt = formatted.WidthIncludingTrailingWhitespace / PxPerPoint;

            var expectedX = fillRect.X + Math.Max(4, (fillRect.Width - realWidthPt) / 2);

            text.X.Should().BeApproximately(expectedX, 0.05,
                "the PDF label position must agree with the same real glyph-width measurement the " +
                "live editor uses to center this label on screen, not a flat 0.52em/char guess");

            // Confirm this label actually exercises the defect: the flat guess and the real
            // measurement must diverge meaningfully, or this assertion would pass by coincidence
            // regardless of which formula production used.
            var flatGuessWidthPt = label.Length * 10 * 0.52;
            Math.Abs(flatGuessWidthPt - realWidthPt).Should().BeGreaterThan(5,
                "the chosen label must make the flat guess and the real measurement diverge enough " +
                "for this test to be a meaningful regression check");
        });
    }

    [Fact]
    public async Task Icon_backed_object_still_renders_as_image_not_text_in_pdf()
    {
        await Dispatch(() =>
        {
            // Sibling/no-regression case: when an icon decodes successfully, BuildPdfEmbeddedObjectOps
            // takes the image branch, which this fix does not touch at all -- no label-width
            // measurement is (or should be) involved.
            var iconObject = EmbeddedObject.Create(
                [1, 2, 3],
                "Excel.Sheet.12",
                new InlineImage(SolidPng(), 24, 24) { AltText = "Workbook icon" },
                widthPt: 48,
                heightPt: 30);
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromEmbeddedObject(iconObject));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(900, 1200));

            var ops = view.BuildPdfContent().Pages.SelectMany(page => page.Ops).ToArray();
            ops.OfType<PdfImage>().Should().ContainSingle();
            ops.OfType<PdfText>().Should().NotContain(t => t.Text == "Excel.Sheet.12");
        });
    }

    private static byte[] SolidPng()
    {
        using var bitmap = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
