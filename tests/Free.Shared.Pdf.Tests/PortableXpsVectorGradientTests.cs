using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using Free.Shared.Pdf;
using Free.Shared.Xps;

namespace Free.Shared.Pdf.Tests;

public sealed class PortableXpsVectorGradientTests
{
    private static readonly XNamespace Xps = "http://schemas.microsoft.com/xps/2005/06";
    private static readonly PdfColor Red = new(255, 0, 0);
    private static readonly PdfColor Green = new(0, 255, 0);
    private static readonly PdfColor Blue = new(0, 0, 255);
    private static readonly PdfColor Fallback = new(12, 34, 56);

    [Fact]
    public void WriteToBytes_UsesAbsoluteVectorBrushesForGradientFillsAndStrokes()
    {
        var gradient = new PdfLinearGradient(24, 30, 48, 60,
            [new(0.75, Blue), new(0.25, Red), new(0.25, Green), new(double.NaN, Fallback)]);
        var contour = new PdfPathContour(new(0, 0),
            [PdfPathSegment.LineTo(new(8, 0)), PdfPathSegment.LineTo(new(8, 6))],
            Closed: true);

        var page = ReadFixedPage(
            new PdfFillRectLinearGradient(10, 12, 60, 40, gradient, Fallback),
            new PdfStrokeRectLinearGradient(10, 12, 60, 40, gradient, Fallback, 2.5),
            new PdfFillEllipseLinearGradient(10, 12, 60, 40, gradient, Fallback),
            new PdfStrokeEllipseLinearGradient(10, 12, 60, 40, gradient, Fallback, 2.5),
            new PdfLineLinearGradient(10, 12, 40, 60, gradient, Fallback, 2.5),
            new PdfPathLinearGradient([contour], gradient, Fallback, gradient, Fallback, 2.5));

        PortableXpsWriter.CanRenderLinearGradient(gradient).Should().BeTrue();
        var paths = page.Descendants(Xps + "Path").ToArray();
        paths.Should().HaveCount(6);
        paths.SelectMany(path => path.Elements()).Select(property => property.Name.LocalName).Should().Equal(
            "Path.Fill", "Path.Stroke", "Path.Fill", "Path.Stroke", "Path.Stroke", "Path.Fill", "Path.Stroke");
        paths.Where(path => path.Element(Xps + "Path.Stroke") is not null)
            .Should().OnlyContain(path => (string?)path.Attribute("StrokeThickness") == "2.5");
        paths.Should().OnlyContain(path => path.Attribute("Fill") == null && path.Attribute("Stroke") == null);
        page.Descendants(Xps + "ImageBrush").Should().BeEmpty();

        foreach (var brush in page.Descendants(Xps + "LinearGradientBrush"))
        {
            ((string?)brush.Attribute("MappingMode")).Should().Be("Absolute");
            ((string?)brush.Attribute("ColorInterpolationMode")).Should().Be("SRgbLinearInterpolation");
            ((string?)brush.Attribute("SpreadMethod")).Should().Be("Pad");
            ((string?)brush.Attribute("StartPoint")).Should().Be("24,70");
            ((string?)brush.Attribute("EndPoint")).Should().Be("48,40");
            brush.Element(Xps + "LinearGradientBrush.GradientStops")!
                .Elements(Xps + "GradientStop")
                .Select(stop => ((string?)stop.Attribute("Offset"), (string?)stop.Attribute("Color")))
                .Should().Equal(("0", "#FF0000"), ("0.25", "#FF0000"), ("0.25", "#00FF00"), ("0.75", "#0000FF"), ("1", "#0000FF"));
        }
    }

    [Fact]
    public void WriteToBytes_KeepsSolidFallbackForGradientsOutsideNativeBounds()
    {
        var valid = new PdfLinearGradient(10, 10, 40, 40, [new(0, Red), new(1, Blue)]);
        PdfLinearGradient[] invalid =
        [
            valid with { EndX = double.PositiveInfinity },
            valid with { EndX = 1e13 },
            valid with { EndX = valid.StartX, EndY = valid.StartY },
            valid with { Stops = [new(double.NaN, Red)] },
            valid with { Stops = Enumerable.Range(0, 101).Select(index => new PdfGradientStop(index / 100d, Red)).ToArray() },
        ];

        foreach (var gradient in invalid)
        {
            PortableXpsWriter.CanRenderLinearGradient(gradient).Should().BeFalse();
            var page = ReadFixedPage(new PdfFillRectLinearGradient(10, 12, 60, 40, gradient, Fallback));
            page.Descendants(Xps + "LinearGradientBrush").Should().BeEmpty();
            var path = page.Descendants(Xps + "Path").Should().ContainSingle().Subject;
            ((string?)path.Attribute("Fill")).Should().Be("#0C2238");
        }
    }

    private static XElement ReadFixedPage(params PdfDrawOp[] operations)
    {
        var bytes = PortableXpsWriter.WriteToBytes(new PdfContentDocument([new PdfContentPage(100, 100, operations)]));
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var pageStream = archive.GetEntry("Documents/1/Pages/1.fpage")!.Open();
        return XElement.Load(pageStream);
    }
}
