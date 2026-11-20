using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using Free.Shared.Pdf.Skia;
using Free.Shared.Xps;
using SkiaSharp;

namespace Free.Shared.Pdf.Tests;

public sealed class PortableXpsWriterCompatibilityTests
{
    private static readonly XNamespace Xps = "http://schemas.microsoft.com/xps/2005/06";
    private static readonly PdfColor Red = new(255, 0, 0);
    private static readonly PdfColor Blue = new(0, 0, 255);
    private static readonly PdfPathContour Contour = new(new(1, 1), [PdfPathSegment.LineTo(new(20, 20))], false);

    [Fact]
    public void GradientOnlyPaths_EmitFillAndStrokeBrushes()
    {
        var gradient = new PdfLinearGradient(0, 0, 20, 20, [new(0, Red), new(1, Blue)]);
        var page = ReadPage(new PdfPathLinearGradient([Contour], gradient, null, gradient, null, 2));
        var path = page.Descendants(Xps + "Path").Should().ContainSingle().Subject;
        path.Element(Xps + "Path.Fill")!.Element(Xps + "LinearGradientBrush").Should().NotBeNull();
        path.Element(Xps + "Path.Stroke")!.Element(Xps + "LinearGradientBrush").Should().NotBeNull();
        path.Attribute("StrokeThickness")!.Value.Should().Be("2");
    }

    [Fact]
    public void Skia_GradientOnlyPathsRenderFillAndStrokeWithoutFallback()
    {
        var gradient = new PdfLinearGradient(10, 10, 60, 10, [new(0, Red), new(1, Blue)]);
        var fillContour = new PdfPathContour(new(10, 10), [PdfPathSegment.LineTo(new(60, 10)), PdfPathSegment.LineTo(new(60, 30)), PdfPathSegment.LineTo(new(10, 30))], true);
        var strokeContour = new PdfPathContour(new(10, 60), [PdfPathSegment.LineTo(new(60, 60))], false);
        var page = new PdfContentPage(100, 100, [new PdfPathLinearGradient([fillContour], gradient, null, null, null, 0),
            new PdfPathLinearGradient([strokeContour], null, null, gradient, null, 6)]);
        using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(new PdfContentDocument([page]), 72).Single());
        bitmap.GetPixel(15, 80).Red.Should().BeGreaterThan(bitmap.GetPixel(15, 80).Blue);
        bitmap.GetPixel(55, 80).Blue.Should().BeGreaterThan(bitmap.GetPixel(55, 80).Red);
        bitmap.GetPixel(15, 40).Red.Should().BeGreaterThan(bitmap.GetPixel(15, 40).Blue);
        bitmap.GetPixel(55, 40).Blue.Should().BeGreaterThan(bitmap.GetPixel(55, 40).Red);
    }

    [Fact]
    public void Skia_InvalidGradientWithoutFallbackThrows()
    {
        var invalid = new PdfLinearGradient(0, 0, 0, 0, [new(0, Red), new(1, Blue)]);
        var document = Document(new PdfPathLinearGradient([Contour], invalid, null, null, null, 0));
        Action render = () => SkiaPdfWriter.RenderPagesToPng(document, 72);
        render.Should().Throw<InvalidOperationException>().WithMessage("*gradient*");
    }

    [Fact]
    public void SolidFallbackOnly_RendersFillAndStrokeAcrossBackends()
    {
        var rectangle = new PdfPathContour(new(10, 10), [PdfPathSegment.LineTo(new(60, 10)), PdfPathSegment.LineTo(new(60, 40)), PdfPathSegment.LineTo(new(10, 40))], true);
        var document = Document(new PdfPathLinearGradient([rectangle], null, Red, null, Blue, 4));
        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(document));
        pdf.Should().Contain("1 0 0 rg").And.Contain("0 0 1 RG");
        pdf.Replace("\r\n", "\n", StringComparison.Ordinal).Should().Contain("\nB\n");
        using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(document, 72).Single());
        bitmap.GetPixel(30, 75).Red.Should().BeGreaterThan(200);
        bitmap.GetPixel(30, 60).Blue.Should().BeGreaterThan(150);
        var path = ReadPage(new PdfPathLinearGradient([rectangle], null, Red, null, Blue, 4))
            .Descendants(Xps + "Path").Should().ContainSingle().Subject;
        path.Attribute("Fill")!.Value.Should().Be("#FF0000");
        path.Attribute("Stroke")!.Value.Should().Be("#0000FF");
    }

    [Fact]
    public void InvalidGradientWithoutFallback_FailsAcrossBackendsEvenWithOtherPaint()
    {
        var invalid = new PdfLinearGradient(0, 0, 0, 0, [new(0, Red), new(1, Blue)]);
        var document = Document(new PdfPathLinearGradient([Contour], null, Red, invalid, null, 2));
        Action portable = () => PortablePdfWriter.WriteToBytes(document);
        portable.Should().Throw<InvalidOperationException>().WithMessage("*gradient*");
        Action skia = () => SkiaPdfWriter.RenderPagesToPng(document, 72);
        skia.Should().Throw<InvalidOperationException>().WithMessage("*gradient*");
        PortableXpsWriter.Analyze(document).IsExportable.Should().BeFalse();
        Action xps = () => PortableXpsWriter.WriteToBytes(document);
        xps.Should().Throw<XpsUnsupportedContentException>();
    }

    [Fact]
    public void FillOnlyPaths_IgnoreUnusedInvalidStrokeDash()
    {
        var invalid = new PdfDashPattern([-1, 0]);
        PdfDrawOp[] operations = [new PdfPath([Contour], Red, null, 0, invalid),
            new PdfPathPattern([Contour], new PdfPatternFill(PdfPatternKind.Horizontal, Red, Blue), null, 0, invalid),
            new PdfPathLinearGradient([Contour], null, Red, null, null, 0, invalid)];
        foreach (var operation in operations)
        {
            var document = Document(operation);
            PortableXpsWriter.Analyze(document).IsExportable.Should().BeTrue();
            Action direct = () => PortableXpsWriter.WriteToBytes(document);
            direct.Should().NotThrow();
            Action skia = () => SkiaXpsWriter.WriteToBytes(document);
            skia.Should().NotThrow();
        }
    }

    [Fact]
    public void InvalidGradientWithoutFallback_IsRejectedDuringAnalysis()
    {
        var gradient = new PdfLinearGradient(0, 0, 0, 0, [new(0, Red), new(1, Blue)]);
        var document = Document(new PdfPathLinearGradient([Contour], gradient, null, null, null, 0));
        PortableXpsWriter.Analyze(document).IsExportable.Should().BeFalse();
        Action write = () => PortableXpsWriter.WriteToBytes(document);
        write.Should().Throw<XpsUnsupportedContentException>();
    }

    [Theory]
    [InlineData(new double[] { 6, 2, 4 }, 1, "3 1 2 3 1 2", "0.5")]
    [InlineData(new double[] { 0, 3 }, 2, "0 1.5", "1")]
    [InlineData(new double[] { 2, 0, 4 }, 0, "1 0 2 1 0 2", null)]
    public void Dash_PreservesOddSequencesZeroLengthsAndPhase(double[] segments, double phase, string expected, string? expectedPhase)
    {
        var page = ReadPage(new PdfStrokeRect(1, 1, 20, 20, Red, 2, new PdfDashPattern(segments, phase)));
        var path = page.Descendants(Xps + "Path").Should().ContainSingle().Subject;
        path.Attribute("StrokeDashArray")!.Value.Should().Be(expected);
        ((string?)path.Attribute("StrokeDashOffset")).Should().Be(expectedPhase);
    }

    [Theory]
    [InlineData(-1d, 1d)]
    [InlineData(double.NaN, 1d)]
    [InlineData(0d, 0d)]
    [InlineData(1e300, 1d)]
    public void InvalidDash_IsRejectedRatherThanChanged(double first, double second)
    {
        var document = Document(new PdfStrokeRect(1, 1, 20, 20, Red, 2, new PdfDashPattern([first, second])));
        PortableXpsWriter.Analyze(document).IsExportable.Should().BeFalse();
        Action write = () => PortableXpsWriter.WriteToBytes(document);
        write.Should().Throw<XpsUnsupportedContentException>();
    }

    [Fact]
    public void UnsupportedEffectsAndUnknownOperations_AreRejectedRecursively()
    {
        PdfDrawOp effect = new PdfEffectGroup(PdfEffectKind.Shadow, 0, 0, 10, 10, new PdfEffectParameters(Red, 1, 0), []);
        foreach (var operation in new PdfDrawOp[] { effect, new UnknownOp(), new PdfClipGroup(0, 0, 10, 10, [new PdfOpacityGroup(1, [effect])]) })
        {
            var document = Document(operation);
            var report = PortableXpsWriter.Analyze(document);
            report.IsExportable.Should().BeFalse();
            report.Requirements.Should().ContainSingle(item => item.StartsWith("Unsupported draw operation:", StringComparison.Ordinal));
            Action write = () => PortableXpsWriter.WriteToBytes(document);
            write.Should().Throw<XpsUnsupportedContentException>();
        }
    }

    [Fact]
    public void PdfAndSkia_PreserveZeroDashSemantics()
    {
        var document = Document(new PdfStrokeRect(10, 10, 60, 60, PdfColor.Black, 2, new PdfDashPattern([0, 6], 1)));
        Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(document)).Should().Contain("[0 6] 1 d");
        using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(document, 72).Single());
        var darkPixels = 0;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).Red < 128) darkPixels++;
        darkPixels.Should().Be(0, "zero-length painted dashes with butt caps have no visible ink");
    }

    private sealed record UnknownOp : PdfDrawOp;
    private static PdfContentDocument Document(PdfDrawOp op) => new([new PdfContentPage(100, 100, [op])]);
    private static XElement ReadPage(PdfDrawOp op)
    {
        using var archive = new ZipArchive(new MemoryStream(PortableXpsWriter.WriteToBytes(Document(op))));
        using var stream = archive.GetEntry("Documents/1/Pages/1.fpage")!.Open();
        return XElement.Load(stream);
    }
}
