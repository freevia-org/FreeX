using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using Free.Shared.Xps;

namespace Free.Shared.Pdf.Tests;

public sealed class PortableXpsPatternBrushTests
{
    private static readonly XNamespace Xps = "http://schemas.microsoft.com/xps/2005/06";
    private static readonly PdfColor Foreground = new(0x12, 0x45, 0x67);
    private static readonly PdfColor Background = new(0xDC, 0xBA, 0x98);

    [Fact]
    public void WriteToBytes_TilesVectorPatternsInsideRectangleAndEllipseFills()
    {
        var page = new PdfContentPage(72, 72, [
            new PdfFillRectPattern(14, 18, 24, 12, new PdfPatternFill(PdfPatternKind.Brick, Foreground, Background, 0.75)),
            new PdfFillEllipsePattern(40, 18, 24, 12, new PdfPatternFill(PdfPatternKind.Dot, Foreground, Background, 0.75)),
        ]);
        var document = new PdfContentDocument([page]);

        PortableXpsWriter.Analyze(document).IsExportable.Should().BeTrue();
        using var file = new ZipArchive(new MemoryStream(PortableXpsWriter.WriteToBytes(document)), ZipArchiveMode.Read);
        using var stream = file.GetEntry("Documents/1/Pages/1.fpage")!.Open();
        var fixedPage = XElement.Load(stream);
        file.Entries.Should().NotContain(entry => entry.FullName.StartsWith("Resources/Images/", StringComparison.Ordinal));

        var brushes = fixedPage.Descendants(Xps + "Path.Fill").Elements(Xps + "VisualBrush").ToArray();
        brushes.Should().HaveCount(2);

        var brick = brushes[0];
        ((string?)brick.Parent!.Parent!.Attribute("Data")).Should().Be("M 14,42 L 38,42 L 38,54 L 14,54 Z");
        ((string?)brick.Attribute("Viewbox")).Should().Be("0,0,12,8");
        ((string?)brick.Attribute("Viewport")).Should().Be("0,0,9,6");
        ((string?)brick.Attribute("ViewboxUnits")).Should().Be("Absolute");
        ((string?)brick.Attribute("ViewportUnits")).Should().Be("Absolute");
        ((string?)brick.Attribute("TileMode")).Should().Be("Tile");
        var brickPrimitives = GetTilePaths(brick);
        brickPrimitives.Should().HaveCount(7);
        ((string?)brickPrimitives[0].Attribute("Fill")).Should().Be("#DCBA98");
        ((string?)brickPrimitives[0].Attribute("Data")).Should().Be("M 0,0 L 12,0 L 12,8 L 0,8 Z");
        ((string?)brickPrimitives[1].Attribute("Stroke")).Should().Be("#124567");
        ((string?)brickPrimitives[1].Attribute("StrokeThickness")).Should().Be("0.5");

        var dot = brushes[1];
        ((string?)dot.Attribute("Viewport")).Should().Be("0,0,6,6");
        var dotPrimitives = GetTilePaths(dot);
        dotPrimitives.Should().HaveCount(2);
        ((string?)dotPrimitives[1].Attribute("Fill")).Should().Be("#124567");
        ((string?)dotPrimitives[1].Attribute("Data")).Should().Be("M 5,4 A 1,1 0 1 1 3,4 A 1,1 0 1 1 5,4 Z");
    }

    [Theory]
    [InlineData(PdfPatternKind.UpDiagonal, "M 0,8 L 8,0")]
    [InlineData(PdfPatternKind.DownDiagonal, "M 0,0 L 8,8")]
    public void Build_PreservesTheRecipeDiagonalDirection(PdfPatternKind kind, string data)
    {
        var brush = PortableXpsPatternBrush.Build(new PdfPatternFill(kind, Foreground, Background));
        var paths = GetTilePaths(brush);

        paths.Should().HaveCount(2);
        ((string?)paths[1].Attribute("Data")).Should().Be(data);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e-15)]
    [InlineData(1e12)]
    public void CanRender_RejectsTileDimensionsOutsideTheXpsCoordinateRange(double unitScale)
    {
        var pattern = new PdfPatternFill(PdfPatternKind.Horizontal, Foreground, Background, unitScale);

        PortableXpsPatternBrush.CanRender(pattern).Should().BeFalse();
    }

    private static XElement[] GetTilePaths(XElement brush) =>
        brush.Element(Xps + "VisualBrush.Visual")!.Element(Xps + "Canvas")!.Elements(Xps + "Path").ToArray();
}
